using System.Collections.Concurrent;
using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class PayPalReservationChecks
{
    internal static readonly IConfiguration Config = new ConfigurationBuilder().Build();

    public static async Task RunAsync()
    {
        await BlocksOtherPayments();
        await ConfirmedFailureReleases();
        await RetryAndReconcile();
        await MismatchedResponse();
        await CancelledRequest();
        await LegacyAndMonthBoundary();
        Console.WriteLine(
            "Single PayPal reservations: capacity before capture, other PayPal/card blocked, terminal release, retry/reconcile, subsidy gross, month rollover and legacy unknown attempts passed"
        );
    }

    private static async Task BlocksOtherPayments()
    {
        await using var db = Memory();
        var f = await Seed(db);
        var gateway = new Gateway { LoseResponse = true };
        var pay = Service(db, gateway);
        var a = await pay.StartAsync(f.First, "reserve-a");
        var b = await pay.StartAsync(f.Second, "reserve-b");
        gateway.BeforeCapture = async () =>
        {
            var p = await db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == f.First);
            Check(
                p.Status == "Verifying"
                    && p.CaptureReservedAt != null
                    && p.ReservedSellerGross == 100m,
                "reservation exists before contacting provider"
            );
        };
        var pending = await pay.CaptureAsync(f.First, a.Id);
        Check(pending.Status == "Verifying", "lost capture response stays unknown");
        gateway.BeforeCapture = null;
        await Reject(() => pay.CaptureAsync(f.Second, b.Id));
        await Reject(() =>
            new CheckoutService(db, Finance(db)).PayCardAsync(
                f.Second,
                "4111111111111111",
                "12/30",
                "blocked-card"
            )
        );
        var blocked = await db.Payments.SingleAsync(x => x.OrderId == f.Second);
        Check(
            blocked.Status == "Pending"
                && blocked.CaptureReservedAt == null
                && blocked.ReservedSellerGross == null,
            "preflight rejection does not create a stuck unknown attempt"
        );
        Check(
            gateway.CaptureCalls == 1
                && !await db.SellerSettlements.AnyAsync()
                && await db.Payments.CountAsync() == 2,
            "insufficient capacity cannot charge provider/card or create settlement"
        );
        await Reject(() => pay.StartAsync(f.First, "replacement-while-unknown"));
        Check(
            await new CheckoutService(db).ExpirePendingAsync() == 0,
            "unknown capture cannot expire"
        );
    }

    private static async Task ConfirmedFailureReleases()
    {
        foreach (var status in new[] { "DECLINED", "DENIED", "VOIDED", "FAILED" })
        {
            await using var db = Memory();
            var f = await Seed(db);
            var gateway = new Gateway { Status = status };
            var pay = Service(db, gateway);
            var a = await pay.StartAsync(f.First, "failure-a");
            var b = await pay.StartAsync(f.Second, "failure-b");
            var failed = await pay.CaptureAsync(f.First, a.Id);
            Check(
                failed.Status == "Failed" && failed.ReservedSellerGross == 100m,
                "failure keeps reservation history but releases active capacity"
            );
            await pay.CaptureAsync(f.First, a.Id);
            await pay.ReconcileAsync(f.First);
            Check(
                gateway.CaptureCalls == 1 && gateway.GetCalls == 0,
                "terminal attempt cannot capture again or revive through reconcile"
            );
            await Reject(() => pay.StartAsync(f.First, "failure-a"));
            Check(
                await db.NotificationOutbox.CountAsync(x =>
                    x.OrderId == f.First && x.EventType == "PaymentFailed"
                ) == 1,
                "failure replay sends one notification"
            );
            gateway.Status = "COMPLETED";
            Check(
                (await pay.CaptureAsync(f.Second, b.Id)).Status == "Succeeded",
                "confirmed failure frees capacity for the second order"
            );
            Check(
                (await db.SellerAccounts.SingleAsync()).MonthlySalesAmount == 100m,
                "only the successful order consumes sales capacity"
            );
        }
    }

    private static async Task RetryAndReconcile()
    {
        foreach (var viaCapture in new[] { false, true })
        {
            await using var db = Memory();
            var f = await Seed(db, subsidy: 20m);
            var gateway = new Gateway { LoseResponse = true };
            var pay = Service(db, gateway);
            var start = await pay.StartAsync(f.First, "retry");
            var unknown = await pay.CaptureAsync(f.First, start.Id);
            var reservedAt = unknown.CaptureReservedAt;
            Check(
                unknown.ReservedSellerGross == 120m,
                "capacity reserves SellerGross including platform subsidy, not buyer cash alone"
            );
            var account = await db.SellerAccounts.SingleAsync();
            account.MonthlySalesLimit = 1m;
            (await db.OrderTables.FindAsync(f.First))!.PaymentExpiresAt =
                DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
            gateway.LoseResponse = false;
            var result = viaCapture
                ? await pay.CaptureAsync(f.First, start.Id)
                : await pay.ReconcileAsync(f.First);
            Check(
                result.Status == "Succeeded" && result.CaptureReservedAt == reservedAt,
                "original authorization survives cap reduction, expiry and retry"
            );
            await pay.CaptureAsync(f.First, start.Id);
            await pay.ReconcileAsync(f.First);
            Check(
                gateway.UniqueCaptures == 1
                    && account.MonthlySalesAmount == 120m
                    && await db.SellerSettlements.CountAsync() == 1,
                "one provider operation, gross increment and settlement despite retries"
            );
            Check(
                await db.FinancialTransactions.CountAsync(x => x.Type == "Sale") == 1
                    && await db.NotificationOutbox.CountAsync(x =>
                        x.OrderId == f.First && x.EventType == "PaymentSucceeded"
                    ) == 1,
                "ledger and success mail are idempotent"
            );
        }
    }

    private static async Task MismatchedResponse()
    {
        await using var db = Memory();
        var f = await Seed(db);
        var gateway = new Gateway { WrongAmount = true };
        var pay = Service(db, gateway);
        var a = await pay.StartAsync(f.First, "mismatch-a");
        var b = await pay.StartAsync(f.Second, "mismatch-b");
        Check(
            (await pay.CaptureAsync(f.First, a.Id)).Status == "Verifying",
            "unverified amount cannot credit seller"
        );
        await Reject(() => pay.CaptureAsync(f.Second, b.Id));
        Check(
            !await db.SellerSettlements.AnyAsync()
                && !await db.NotificationOutbox.AnyAsync(x => x.EventType == "PaymentFailed"),
            "uncertainty neither releases capacity nor emits confirmed failure"
        );
        gateway.WrongAmount = false;
        Check(
            (await pay.ReconcileAsync(f.First)).Status == "Succeeded",
            "corrected provider lookup settles the existing reservation"
        );
    }

    private static async Task LegacyAndMonthBoundary()
    {
        foreach (var legacy in new[] { false, true })
        {
            await using var db = Memory();
            var f = await Seed(db);
            var gateway = new Gateway { LoseResponse = true };
            var pay = Service(db, gateway);
            var a = await pay.StartAsync(f.First, "legacy-a");
            var b = await pay.StartAsync(f.Second, "legacy-b");
            var p = await pay.CaptureAsync(f.First, a.Id);
            var account = await db.SellerAccounts.SingleAsync();
            var previousMonth = new DateTime(
                DateTime.UtcNow.Year,
                DateTime.UtcNow.Month,
                1
            ).AddMonths(-1);
            account.SalesMonth = previousMonth;
            account.MonthlySalesAmount = 30m;
            p.CaptureReservedAt = legacy ? null : previousMonth;
            if (legacy)
                p.ReservedSellerGross = null;
            await db.SaveChangesAsync();
            await Reject(() => pay.CaptureAsync(f.Second, b.Id));
            gateway.LoseResponse = false;
            var calls = gateway.CaptureCalls;
            Check(
                (await pay.CaptureAsync(f.First, a.Id)).Status == "Succeeded",
                "old unknown attempt can resolve without losing its money"
            );
            Check(
                !legacy || gateway.CaptureCalls == calls && gateway.GetCalls == 1,
                "unreserved legacy attempt is queried, not granted a fresh capture authorization"
            );
            Check(
                (await db.SellerAccounts.SingleAsync()).MonthlySalesAmount == 100m,
                "UTC month resets sales but does not discard outstanding capacity"
            );
        }
    }

    private static async Task CancelledRequest()
    {
        await using var db = Memory();
        var f = await Seed(db);
        var gateway = new Gateway();
        var pay = Service(db, gateway);
        var start = await pay.StartAsync(f.First, "cancelled-request");
        using var cancellation = new CancellationTokenSource();
        gateway.BeforeCapture = () =>
        {
            cancellation.Cancel();
            throw new TaskCanceledException(
                "Caller stopped waiting after capture began",
                null,
                cancellation.Token
            );
        };
        try
        {
            await pay.CaptureAsync(f.First, start.Id, cancellation.Token);
            throw new Exception("Caller cancellation was swallowed");
        }
        catch (TaskCanceledException) when (cancellation.IsCancellationRequested) { }
        var pending = await db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == f.First);
        Check(
            pending.Status == "Verifying"
                && pending.ReservedSellerGross == 100m
                && pending.CaptureReservedAt != null,
            "caller cancellation cannot discard a committed reservation"
        );
        gateway.BeforeCapture = null;
        Check(
            (await pay.ReconcileAsync(f.First)).Status == "Succeeded"
                && gateway.UniqueCaptures == 1,
            "worker can reconcile after the caller stopped waiting"
        );
    }

    internal static ApplicationDbContext Memory() =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    internal static SellerFinanceService Finance(ApplicationDbContext db) => new(db, Config);

    internal static PayPalPaymentService Service(ApplicationDbContext db, Gateway gateway) =>
        new(db, gateway, Config, Finance(db));

    internal static async Task<(int Buyer, int Seller, int First, int Second)> Seed(
        ApplicationDbContext db,
        decimal subsidy = 0m
    )
    {
        var token = Guid.NewGuid().ToString("N");
        var buyer = new User
        {
            Role = "buyer",
            Email = "reservation-buyer-" + token + "@example.test",
        };
        var seller = new User
        {
            Role = "seller",
            Email = "reservation-seller-" + token + "@example.test",
        };
        db.Users.AddRange(buyer, seller);
        await db.SaveChangesAsync();
        db.SellerAccounts.Add(
            new SellerAccount
            {
                SellerId = seller.Id,
                Level = 1,
                MonthlySalesLimit = 150m,
                SalesMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1),
                UpdatedAt = DateTime.UtcNow,
            }
        );
        var orders = Enumerable
            .Range(0, 2)
            .Select(_ => new OrderTable
            {
                BuyerId = buyer.Id,
                SellerId = seller.Id,
                Status = "AwaitingPayment",
                TotalPrice = 100m,
                OrderDate = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PaymentExpiresAt = DateTime.UtcNow.AddMinutes(15),
                PricingSchemaVersion = 1,
                PlatformSubsidy = subsidy,
                SellerGrossSnapshot = 100m + subsidy,
            })
            .ToArray();
        db.OrderTables.AddRange(orders);
        await db.SaveChangesAsync();
        return (buyer.Id, seller.Id, orders[0].Id, orders[1].Id);
    }

    internal sealed class Gateway : IPayPalGateway
    {
        private readonly ConcurrentDictionary<
            string,
            (string Key, decimal Amount, string Currency)
        > orders = new();
        private readonly ConcurrentDictionary<string, string> captures = new();
        private int captureCalls,
            getCalls;
        public int CaptureCalls => captureCalls;
        public int GetCalls => getCalls;
        public int UniqueCaptures => captures.Count;
        public bool LoseResponse,
            WrongAmount;
        public string Status = "COMPLETED";
        public Func<Task>? BeforeCapture;

        public Task<PayPalCreated> CreateAsync(
            decimal amount,
            string currency,
            string requestId,
            string returnUrl,
            string cancelUrl,
            CancellationToken ct
        )
        {
            var id = "RESERVATION-" + Guid.NewGuid().ToString("N");
            orders[id] = (requestId, amount, currency);
            return Task.FromResult(new PayPalCreated(id, "https://example.test/approve"));
        }

        public async Task<PayPalCaptured> CaptureAsync(string id, string key, CancellationToken ct)
        {
            Interlocked.Increment(ref captureCalls);
            captures.AddOrUpdate(
                id,
                key,
                (_, prior) =>
                    prior == key ? prior : throw new Exception("Capture retry changed provider key")
            );
            if (BeforeCapture != null)
                await BeforeCapture();
            if (LoseResponse)
                throw new HttpRequestException("Simulated capture response lost");
            return Result(id);
        }

        public Task<PayPalCaptured> GetAsync(string id, CancellationToken ct)
        {
            Interlocked.Increment(ref getCalls);
            return Task.FromResult(Result(id));
        }

        private PayPalCaptured Result(string id) =>
            new(
                Status,
                Status == "COMPLETED" ? "CAP-" + id : null,
                orders[id].Amount + (WrongAmount ? 1m : 0m),
                orders[id].Currency
            );

        public Task<string> RefundAsync(
            string id,
            decimal amount,
            string currency,
            string key,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }

    internal static void Check(bool value, string message) =>
        ReturnAutomationChecks.Check(value, message);

    internal static async Task Reject(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new Exception("Expected preflight rejection before charging or changing the attempt");
    }
}
