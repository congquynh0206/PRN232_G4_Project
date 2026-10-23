using System.Text.Json;
using G4.Application.Integrations;
using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Integrations.PayPal;
using G4.Infrastructure.Integrations.Refunds;
using G4.Infrastructure.Payments;
using G4.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class PayPalCartChecks
{
    internal static void Check(bool yes, string message) =>
        SellerRegistrationChecks.Check(yes, message);

    public static async Task RunAsync()
    {
        Parse();
        await Success();
        await Unknown();
        await Partial();
        await CancelAndExpiry();
        await WrongResponse();
        await Reservation();
        Console.WriteLine(
            "PayPal cart: one approval/multiple captures, ownership, retries, partial results, unknown capture/reconciliation, cancellation, reservation and refund routing passed"
        );
    }

    private static void Parse()
    {
        using var json = JsonDocument.Parse(
            """{"id":"PP1","status":"COMPLETED","purchase_units":[{"reference_id":"order-2","payments":{"captures":[{"id":"C2","status":"COMPLETED","amount":{"value":"20.00","currency_code":"USD"}}]}},{"reference_id":"order-1","payments":{"captures":[{"id":"C1","status":"COMPLETED","amount":{"value":"10.00","currency_code":"USD"}}]}}]}"""
        );
        var r = PayPalSandboxGateway.ParseBatchCapture(json.RootElement);
        Check(
            r.Units.Count == 2 && r.Units.Single(x => x.ReferenceId == "order-1").CaptureId == "C1",
            "all capture units parsed by reference, not array position"
        );
    }

    internal static async Task<(int Buyer, int[] Orders, string Key)> Seed(ApplicationDbContext db)
    {
        var users = new[]
        {
            new User { Role = "buyer", Email = Guid.NewGuid() + "@example.test" },
            new User { Role = "seller", Email = Guid.NewGuid() + "@example.test" },
            new User { Role = "seller", Email = Guid.NewGuid() + "@example.test" },
        };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        var orders = new[] { 10m, 20m }
            .Select(
                (amount, index) =>
                    new OrderTable
                    {
                        BuyerId = users[0].Id,
                        SellerId = users[index + 1].Id,
                        Status = "AwaitingPayment",
                        TotalPrice = amount,
                        OrderDate = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        PaymentExpiresAt = DateTime.UtcNow.AddMinutes(15),
                    }
            )
            .ToArray();
        db.OrderTables.AddRange(orders);
        await db.SaveChangesAsync();
        var key = Guid.NewGuid().ToString();
        db.CheckoutBatches.Add(
            new CheckoutBatch
            {
                Key = key,
                BuyerId = users[0].Id,
                RequestJson = "{}",
                OrderIdsJson = JsonSerializer.Serialize(orders.Select(o => o.Id)),
            }
        );
        await db.SaveChangesAsync();
        return (users[0].Id, orders.Select(o => o.Id).ToArray(), key);
    }

    private static async Task Success()
    {
        await using var f = new Fixture();
        await f.Init();
        var s = f.SeedData;
        var start = await f.Pay.StartAsync(s.Buyer, new(s.Key, "one-approval"));
        Check(
            start.Orders.Count == 2 && f.Gateway.Creates == 1 && f.Gateway.Units.Count == 2,
            "one provider order contains both seller units"
        );
        Check(
            (await f.Pay.StartAsync(s.Buyer, new(s.Key, "one-approval"))).Id == start.Id
                && f.Gateway.Creates == 1,
            "start retry returns same approval without new provider order"
        );
        await SellerRegistrationChecks.Reject<UnauthorizedAccessException>(() =>
            f.Pay.GetAsync(s.Buyer + 999, start.Id)
        );
        await SellerRegistrationChecks.Reject<UnauthorizedAccessException>(() =>
            f.Pay.CaptureAsync(s.Buyer, start.Id, "WRONG")
        );
        var result = await f.Pay.CaptureAsync(s.Buyer, start.Id, "PP-1");
        Check(
            result.Status == "Succeeded"
                && result.Orders.All(o => o.PaymentStatus == "Succeeded")
                && f.Gateway.Captures == 1,
            "single capture call pays both local orders"
        );
        await f.Pay.CaptureAsync(s.Buyer, start.Id, "PP-1");
        Check(
            f.Gateway.Captures == 1
                && await f.Db.SellerSettlements.CountAsync() == 2
                && await f.Db.FinancialTransactions.CountAsync(x => x.Type == "Sale") == 2,
            "capture replay cannot duplicate money or settlement"
        );
        var payments = await f.Db.Payments.OrderBy(p => p.OrderId).ToArrayAsync();
        Check(
            payments.Select(p => p.ProviderTransactionId).Distinct().Count() == 2,
            "capture reference is separate for each order"
        );
        var refunds = new RefundFake();
        await new RefundGateway(refunds).RefundAsync(payments[1], 5, "refund-second", default);
        Check(
            refunds.CaptureId == payments[1].ProviderTransactionId && refunds.Amount == 5,
            "refund routes only to the second order's capture"
        );
    }

    private static async Task Unknown()
    {
        await using var f = new Fixture();
        await f.Init();
        var s = f.SeedData;
        var start = await f.Pay.StartAsync(s.Buyer, new(s.Key, "unknown"));
        f.Gateway.LoseCaptureResponse = true;
        Check(
            (await f.Pay.CaptureAsync(s.Buyer, start.Id, "PP-1")).Status == "Verifying",
            "lost capture response keeps both orders reserved"
        );
        await SellerRegistrationChecks.Reject<InvalidOperationException>(() =>
            f.Pay.CancelAsync(s.Buyer, start.Id)
        );
        await SellerRegistrationChecks.Reject<InvalidOperationException>(() =>
            new G4.Infrastructure.Checkout.CheckoutService(
                f.Db,
                config: f.Config,
                finance: f.Finance
            ).PayCardAsync(s.Orders[0], "4111111111111111", "12/30", "cannot-double-pay")
        );
        foreach (var o in await f.Db.OrderTables.ToListAsync())
            o.PaymentExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync();
        Check(
            (await f.Pay.ReconcileAsync(s.Buyer, start.Id)).Status == "Succeeded",
            "reconciliation completes actual captured units after nominal expiry"
        );
    }

    private static async Task Partial()
    {
        await using var f = new Fixture();
        await f.Init();
        var s = f.SeedData;
        var start = await f.Pay.StartAsync(s.Buyer, new(s.Key, "partial"));
        f.Gateway.DeclineReference = "order-" + s.Orders[1];
        Check(
            (await f.Pay.CaptureAsync(s.Buyer, start.Id, "PP-1")).Status == "PartiallyPaid",
            "partial provider response keeps successful unit"
        );
        f.Gateway.DeclineReference = null;
        var second = await f.Pay.StartAsync(s.Buyer, new(s.Key, "retry-unpaid"));
        Check(
            f.Gateway.Units.Count == 1 && f.Gateway.Units[0].ReferenceId == "order-" + s.Orders[1],
            "resume only includes unpaid order"
        );
        Check(
            (await f.Pay.CaptureAsync(s.Buyer, second.Id, "PP-2")).Orders.All(o =>
                o.PaymentStatus == "Succeeded"
            ),
            "resumed checkout shows successful results for entire source cart"
        );
        Check(
            await f.Db.SellerSettlements.CountAsync() == 2,
            "resume does not resettle already paid unit"
        );
    }

    private static async Task CancelAndExpiry()
    {
        await using var f = new Fixture();
        await f.Init();
        var s = f.SeedData;
        var start = await f.Pay.StartAsync(s.Buyer, new(s.Key, "cancel"));
        Check(
            (await f.Pay.CancelAsync(s.Buyer, start.Id)).Status == "Cancelled",
            "cancel releases pending cart payment"
        );
        await f.Pay.CaptureAsync(s.Buyer, start.Id, "PP-1");
        Check(f.Gateway.Captures == 0, "cancelled approval cannot capture");
        var retry = await f.Pay.StartAsync(s.Buyer, new(s.Key, "after-cancel"));
        (await f.Db.OrderTables.FirstAsync()).PaymentExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await f.Db.SaveChangesAsync();
        await SellerRegistrationChecks.Reject<InvalidOperationException>(() =>
            f.Pay.CaptureAsync(s.Buyer, retry.Id, "PP-2")
        );
        Check(
            f.Gateway.Captures == 0,
            "expired unit prevents entire remote capture before charging"
        );
    }

    private static async Task WrongResponse()
    {
        await using var f = new Fixture();
        await f.Init();
        var s = f.SeedData;
        var start = await f.Pay.StartAsync(s.Buyer, new(s.Key, "wrong-amount"));
        f.Gateway.WrongAmount = true;
        Check(
            (await f.Pay.CaptureAsync(s.Buyer, start.Id, "PP-1")).Status == "Verifying"
                && !await f.Db.SellerSettlements.AnyAsync(),
            "one mismatched amount prevents crediting an unverified cart response"
        );
        f.Gateway.WrongAmount = false;
        var correct = await f.Pay.ReconcileAsync(s.Buyer, start.Id);
        Check(
            correct.Status == "Succeeded" && correct.Message == null,
            "later verified response clears stale warning and reconciles"
        );
    }

    private static async Task Reservation()
    {
        await using var f = new Fixture();
        await f.Init();
        var s = f.SeedData;
        var start = await f.Pay.StartAsync(s.Buyer, new(s.Key, "reserved"));
        f.Gateway.BeforeCapture = async () =>
        {
            foreach (var a in await f.Db.SellerAccounts.ToListAsync())
            {
                a.Level = 1;
                a.MonthlySalesLimit = 1;
            }
            await f.Db.SaveChangesAsync();
        };
        Check(
            (await f.Pay.CaptureAsync(s.Buyer, start.Id, "PP-1")).Status == "Succeeded",
            "captured amount honors pre-capture capacity reservation after a concurrent level downgrade"
        );
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db = new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        public IConfiguration Config = new ConfigurationBuilder().Build();
        public BatchFake Gateway = new();
        public SellerFinanceService Finance = null!;
        public PayPalCartPaymentService Pay = null!;
        public (int Buyer, int[] Orders, string Key) SeedData;

        public async Task Init()
        {
            await Db.Database.EnsureCreatedAsync();
            SeedData = await Seed(Db);
            Finance = new(Db, Config);
            Pay = new(Db, Gateway, Config, Finance);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    internal sealed class BatchFake : IPayPalBatchGateway
    {
        public int Creates,
            Captures;
        public IReadOnlyList<PayPalPurchaseUnit> Units = [];
        public string Prefix = "PP";
        private readonly Dictionary<string, PayPalCreated> requests = new();
        private readonly Dictionary<string, IReadOnlyList<PayPalPurchaseUnit>> providerUnits =
            new();
        public bool LoseCaptureResponse,
            WrongAmount;
        public string? DeclineReference;
        public Func<Task>? BeforeCapture;

        public Task<PayPalCreated> CreateBatchAsync(
            IReadOnlyList<PayPalPurchaseUnit> units,
            string requestId,
            string returnUrl,
            string cancelUrl,
            CancellationToken ct
        )
        {
            lock (requests)
            {
                if (requests.TryGetValue(requestId, out var prior))
                    return Task.FromResult(prior);
                Creates++;
                Units = units;
                var result = new PayPalCreated(
                    Prefix + "-" + Creates,
                    "https://www.sandbox.paypal.com/checkoutnow?token=" + Prefix + "-" + Creates
                );
                requests.Add(requestId, result);
                providerUnits.Add(result.Id, units);
                return Task.FromResult(result);
            }
        }

        public async Task<PayPalBatchCaptured> CaptureBatchAsync(
            string id,
            string key,
            CancellationToken ct
        )
        {
            Captures++;
            if (BeforeCapture != null)
                await BeforeCapture();
            if (LoseCaptureResponse)
                throw new HttpRequestException("Capture response lost");
            return Result(id);
        }

        public Task<PayPalBatchCaptured> GetBatchAsync(string id, CancellationToken ct) =>
            Task.FromResult(Result(id));

        private PayPalBatchCaptured Result(string id) =>
            new(
                id,
                "COMPLETED",
                providerUnits[id]
                    .Reverse()
                    .Select(u => new PayPalUnitCapture(
                        u.ReferenceId,
                        u.ReferenceId == DeclineReference ? "DECLINED" : "COMPLETED",
                        id + "-CAP-" + u.ReferenceId,
                        u.Amount + (WrongAmount ? 1 : 0),
                        u.Currency
                    ))
                    .ToArray()
            );
    }

    private sealed class RefundFake : IPayPalGateway
    {
        public string? CaptureId;
        public decimal Amount;

        public Task<string> RefundAsync(
            string id,
            decimal amount,
            string currency,
            string key,
            CancellationToken ct
        )
        {
            CaptureId = id;
            Amount = amount;
            return Task.FromResult("REFUND-2");
        }

        public Task<PayPalCreated> CreateAsync(
            decimal a,
            string c,
            string k,
            string r,
            string x,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<PayPalCaptured> CaptureAsync(string id, string key, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<PayPalCaptured> GetAsync(string id, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
