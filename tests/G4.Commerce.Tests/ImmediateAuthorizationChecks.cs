using G4.Domain.Entities;
using G4.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

internal static class ImmediateAuthorizationChecks
{
    public static async Task RunAsync()
    {
        await CaptureTimeout();
        await AuthorizationTimeout();
        await LosingAuthorizationAndVoidRetry();
        await CaptureFailure();
        await UnexpectedCaptureAndRefundRetry();
        await PriceChangedAfterCapture();
        await PersistenceFailure(recover: true);
        await PersistenceFailure(recover: false);
        await PartialRefunds();
        await ExpiredProcessingLease();
        await ConflictingCheckoutKey();
        Console.WriteLine("Immediate authorization saga passed: durable provider, PAID-only insert, "
            + "authorization/capture timeout recovery, loser VOID/no capture, capture failure, "
            + "refund retry/partial amounts, expired leases, key collision, changed price, "
            + "database failure recovery/compensation and no open "
            + "SQL transaction at gateway calls. InMemory functional tests; SQL races run separately.");
    }

    private static readonly IConfiguration Config = new ConfigurationBuilder().Build();

    internal static ImmediateCheckoutService Service(ApplicationDbContext db, IImmediateCardGateway gateway) =>
        new(db, new CheckoutService(db, new SellerFinanceService(db, Config), Config), gateway);

    private static async Task<Fixture> NewFixture()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new PaidInsertGuard()).Options;
        await using var db = new ApplicationDbContext(options);
        var f = await ImmediateCheckoutChecks.Seed(db, 1);
        var session = await ImmediateCheckoutChecks.Start(db, f.Product, f.Buyer, f.Address);
        var provider = new SimulatedImmediateCardGateway(
            new ImmediateCheckoutChecks.ContextFactory(options));
        return new(options, session, f.Buyer, f.Product, provider);
    }

    private static async Task CaptureTimeout()
    {
        var f = await NewFixture();
        var gateway = new FaultGateway(f.Gateway) { TimeoutCaptureOnce = true };
        await using (var db = new ApplicationDbContext(f.Options))
        {
            gateway.CheckNoTransaction = () => Check(db.Database.CurrentTransaction == null,
                "gateway capture is outside the SQL transaction");
            var attempt = await Service(db, gateway).PayCardAsync(f.Buyer, f.Session, Card("capture"));
            Check(attempt.Status == "Verifying" && attempt.InventoryHeld,
                "unknown capture retains inventory and blocks new attempt");
            Check(!await db.OrderTables.AnyAsync(), "no order before confirmed capture");
        }
        await using (var db = new ApplicationDbContext(f.Options))
        {
            gateway.CheckNoTransaction = () => Check(db.Database.CurrentTransaction == null,
                "gateway reconcile is outside the SQL transaction");
            var paid = await Service(db, gateway).PayCardAsync(f.Buyer, f.Session, Card("new-key"));
            Check(paid.Status == "Succeeded" && gateway.CaptureCalls == 1,
                "new key reconciles existing capture without double charging");
            Check(await db.ImmediatePaymentAttempts.CountAsync() == 1, "one attempt while unknown");
            Check((await db.OrderTables.SingleAsync()).Status == "Paid", "first insert PAID");
        }
    }

    private static async Task AuthorizationTimeout()
    {
        var f = await NewFixture();
        var gateway = new FaultGateway(f.Gateway) { TimeoutAuthorizationOnce = true };
        await using var db = new ApplicationDbContext(f.Options);
        var attempt = await Service(db, gateway).PayCardAsync(f.Buyer, f.Session, Card("auth"));
        Check(attempt.Status == "Verifying" && !attempt.InventoryHeld, "authorization unknown");
        var paid = await Service(db, gateway).ReconcileAsync(f.Buyer, f.Session);
        Check(paid.Status == "Succeeded" && gateway.AuthorizationCalls == 1,
            "authorize is queried before replay and never duplicated");
    }

    private static async Task LosingAuthorizationAndVoidRetry()
    {
        var f = await NewFixture();
        var gateway = new FaultGateway(f.Gateway) { VoidFailures = 1 };
        gateway.AfterAuthorization = async () =>
        {
            // Another buyer took the last unit while this authorization was in flight.
            await using var other = new ApplicationDbContext(f.Options);
            (await other.Inventories.SingleAsync()).Quantity = 0;
            await other.SaveChangesAsync();
        };
        await using var db = new ApplicationDbContext(f.Options);
        var pending = await Service(db, gateway).PayCardAsync(f.Buyer, f.Session, Card("loser"));
        Check(pending.Status == "Verifying" && pending.Stage == "Voiding",
            "failed void remains durable and retryable");
        Check(gateway.CaptureCalls == 0, "loser must never capture");
        var lost = await Service(db, gateway).ReconcileAsync(f.Buyer, f.Session);
        Check(lost.Status == "Failed" && lost.Stage == "Voided" && lost.ErrorCode == "OutOfStock",
            "loser authorization is actually voided");
        Check(!await db.OrderTables.AnyAsync() && gateway.VoidCalls == 2, "no orphan loser order");
    }

    private static async Task CaptureFailure()
    {
        var f = await NewFixture();
        var gateway = new FaultGateway(f.Gateway) { FailCapture = true };
        await using var db = new ApplicationDbContext(f.Options);
        var failed = await Service(db, gateway).PayCardAsync(f.Buyer, f.Session, Card("capture-fail"));
        Check(failed.Stage == "Voided" && failed.Status == "Failed", "failed capture voids auth");
        Check(!failed.InventoryHeld && !await db.OrderTables.AnyAsync(), "no failed capture order");
        Check((await db.Inventories.AsNoTracking().SingleAsync()).Quantity == 1,
            "inventory released once after confirmed void");
    }

    private static async Task UnexpectedCaptureAndRefundRetry()
    {
        var f = await NewFixture();
        var gateway = new FaultGateway(f.Gateway) { UnexpectedCapture = true, RefundFailures = 1 };
        await using var db = new ApplicationDbContext(f.Options);
        var pending = await Service(db, gateway).PayCardAsync(f.Buyer, f.Session, Card("unexpected"));
        Check(pending.Status == "Verifying" && pending.Stage == "Refunding",
            "unexpected capture without stock claim goes to refund");
        var refunded = await Service(db, gateway).ReconcileAsync(f.Buyer, f.Session);
        Check(refunded.Stage == "Refunded" && refunded.Status == "Failed",
            "refund failure is retried and confirmed");
        Check(!await db.OrderTables.AnyAsync() && gateway.RefundCalls == 2,
            "no paid order for unexpected capture");
    }

    private static async Task PriceChangedAfterCapture()
    {
        var f = await NewFixture();
        var gateway = new FaultGateway(f.Gateway);
        gateway.AfterCapture = async () =>
        {
            await using var other = new ApplicationDbContext(f.Options);
            (await other.Products.SingleAsync()).Price = 99m;
            await other.SaveChangesAsync();
        };
        await using var db = new ApplicationDbContext(f.Options);
        var refunded = await Service(db, gateway).PayCardAsync(f.Buyer, f.Session, Card("price"));
        Check(refunded.Stage == "Refunded" && !await db.OrderTables.AnyAsync(),
            "changed quote after capture is compensated");
        Check((await db.Inventories.AsNoTracking().SingleAsync()).Quantity == 1,
            "compensated stock restored");
    }

    private static async Task PersistenceFailure(bool recover)
    {
        var f = await NewFixture();
        var failureOptions = new DbContextOptionsBuilder<ApplicationDbContext>(f.Options)
            .AddInterceptors(new FailPaidInsert()).Options;
        await using (var db = new ApplicationDbContext(failureOptions))
        {
            var result = await Service(db, f.Gateway).PayCardAsync(f.Buyer, f.Session, Card("db"));
            Check(result.Status == "Verifying" && result.Stage == "Captured",
                "successful capture survives database order failure");
        }
        await using var retry = new ApplicationDbContext(recover ? f.Options : failureOptions);
        if (recover)
        {
            var paid = await Service(retry, f.Gateway).ReconcileAsync(f.Buyer, f.Session);
            Check(paid.Status == "Succeeded" && await retry.OrderTables.CountAsync() == 1,
                "reconciliation creates one paid order after transient DB failure");
        }
        else
        {
            for (var index = 0; index < 4; index++)
                await Service(retry, f.Gateway).MaintainAsync();
            var failed = await retry.ImmediatePaymentAttempts.AsNoTracking().SingleAsync();
            Check(failed.Stage == "Refunded" && !await retry.OrderTables.AnyAsync(),
                "persistent database failure refunds instead of losing money");
        }
    }

    private static async Task PartialRefunds()
    {
        var f = await NewFixture();
        await using var db = new ApplicationDbContext(f.Options);
        var paid = await Service(db, f.Gateway).PayCardAsync(f.Buyer, f.Session, Card("partial"));
        var payment = await db.Payments.SingleAsync(p => p.OrderId == paid.OrderId);
        var refunds = new G4.Infrastructure.Integrations.Refunds.RefundGateway(
            new TestPayPalGateway(), immediateCards: f.Gateway);
        await refunds.RefundAsync(payment, 3m, "partial-a", default);
        await refunds.RefundAsync(payment, 3m, "partial-a", default);
        var partial = await f.Gateway.GetAsync("IP-" + paid.Id, default);
        Check(partial.Status == "PartiallyRefunded" && partial.RefundedAmount == 3m,
            "partial refund adapter preserves amount and idempotency");
        await refunds.RefundAsync(payment, payment.Amount!.Value - 3m, "remaining", default);
        var complete = await f.Gateway.GetAsync("IP-" + paid.Id, default);
        Check(complete.Status == "Refunded" && complete.RefundedAmount == payment.Amount,
            "refund never exceeds captured total");
        await ImmediateCheckoutChecks.Reject(() =>
            refunds.RefundAsync(payment, 1m, "new-refund-after-full", default));
        await ImmediateCheckoutChecks.Reject(() =>
            refunds.RefundAsync(payment, 4m, "partial-a", default));
    }

    private static async Task ExpiredProcessingLease()
    {
        var f = await NewFixture();
        await using var db = new ApplicationDbContext(f.Options);
        var stored = await db.ImmediateCheckouts.SingleAsync();
        var request = System.Text.Json.JsonSerializer.Deserialize<CheckoutRequest>(stored.RequestJson)!;
        stored.Status = "Verifying";
        db.ImmediatePaymentAttempts.Add(new ImmediatePaymentAttempt
        {
            CheckoutId = f.Session,
            Key = "crashed-before-authorization",
            Stage = "Authorizing",
            AuthorizationOutcome = "Succeeded",
            Amount = request.ExpectedTotal!.Value,
            ProcessingToken = Guid.NewGuid(),
            ProcessingUntil = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await Service(db, f.Gateway).MaintainAsync();
        Check((await db.ImmediatePaymentAttempts.AsNoTracking().SingleAsync()).Status == "Succeeded",
            "worker resumes expired lease without card data after process restart");
    }

    private static async Task ConflictingCheckoutKey()
    {
        var f = await NewFixture();
        await using var db = new ApplicationDbContext(f.Options);
        var session = await db.ImmediateCheckouts.SingleAsync();
        var request = System.Text.Json.JsonSerializer.Deserialize<CheckoutRequest>(session.RequestJson)!;
        db.OrderTables.Add(new OrderTable
        {
            BuyerId = f.Buyer,
            SellerId = (await db.Products.SingleAsync()).SellerId,
            AddressId = request.AddressId,
            CheckoutKey = request.CheckoutKey,
            Status = "AwaitingPayment",
            TotalPrice = request.ExpectedTotal,
            OrderItems = [new OrderItem { ProductId = f.Product, Quantity = 1, UnitPrice = 10m }],
        });
        await db.SaveChangesAsync();
        var result = await Service(db, f.Gateway).PayCardAsync(f.Buyer, f.Session, Card("collision"));
        Check(result.Status == "Failed" && result.Stage == "Refunded",
            "a foreign workflow key cannot produce a false paid result");
        Check((await db.OrderTables.SingleAsync()).Status == "AwaitingPayment"
            && !await db.Payments.AnyAsync(), "historical order is not altered");
    }

    private sealed record Fixture(
        DbContextOptions<ApplicationDbContext> Options, Guid Session, int Buyer, int Product,
        IImmediateCardGateway Gateway
    );

    internal sealed class FaultGateway(IImmediateCardGateway inner) : IImmediateCardGateway
    {
        public bool TimeoutAuthorizationOnce;
        public bool TimeoutCaptureOnce;
        public bool FailCapture;
        public bool UnexpectedCapture;
        public int VoidFailures;
        public int RefundFailures;
        public int AuthorizationCalls;
        public int CaptureCalls;
        public int VoidCalls;
        public int RefundCalls;
        public Action? CheckNoTransaction;
        public Func<Task>? AfterAuthorization;
        public Func<Task>? AfterCapture;

        public Task<CardAuthorizationState> GetAsync(string key, CancellationToken ct)
        {
            CheckNoTransaction?.Invoke();
            return inner.GetAsync(key, ct);
        }

        public async Task<CardAuthorizationState> AuthorizeAsync(
            string key, decimal amount, string outcome, CancellationToken ct)
        {
            CheckNoTransaction?.Invoke();
            AuthorizationCalls++;
            var result = await inner.AuthorizeAsync(key, amount, outcome, ct);
            if (AfterAuthorization != null)
                await AfterAuthorization();
            if (UnexpectedCapture)
                result = await inner.CaptureAsync(key, ct);
            if (TimeoutAuthorizationOnce)
            {
                TimeoutAuthorizationOnce = false;
                throw new TimeoutException("Authorization response lost");
            }
            return result;
        }

        public async Task<CardAuthorizationState> CaptureAsync(string key, CancellationToken ct)
        {
            CheckNoTransaction?.Invoke();
            CaptureCalls++;
            if (FailCapture)
            {
                var authorized = await inner.GetAsync(key, ct);
                return authorized with { Status = "CaptureFailed" };
            }
            var result = await inner.CaptureAsync(key, ct);
            if (AfterCapture != null)
                await AfterCapture();
            if (TimeoutCaptureOnce)
            {
                TimeoutCaptureOnce = false;
                throw new TimeoutException("Capture response lost");
            }
            return result;
        }

        public Task<CardAuthorizationState> VoidAsync(string key, CancellationToken ct)
        {
            CheckNoTransaction?.Invoke();
            VoidCalls++;
            if (VoidFailures-- > 0)
                throw new HttpRequestException("Void unavailable");
            return inner.VoidAsync(key, ct);
        }

        public Task<CardAuthorizationState> RefundAsync(
            string key, CancellationToken ct, decimal? amount = null, string? refundKey = null)
        {
            CheckNoTransaction?.Invoke();
            RefundCalls++;
            if (RefundFailures-- > 0)
                throw new HttpRequestException("Refund unavailable");
            return inner.RefundAsync(key, ct, amount, refundKey);
        }
    }

    private sealed class PaidInsertGuard : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            foreach (var order in data.Context!.ChangeTracker.Entries<OrderTable>()
                .Where(e => e.State == EntityState.Added && e.Entity.ImmediatePaymentInProgress))
                Check(order.Entity.Status == "Paid", "INSERT of an unpaid immediate order rejected");
            return base.SavingChangesAsync(data, result, cancellationToken);
        }
    }

    private sealed class FailPaidInsert : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (data.Context!.ChangeTracker.Entries<OrderTable>()
                .Any(e => e.State == EntityState.Added && e.Entity.ImmediatePaymentInProgress))
                throw new DbUpdateException("Injected order persistence failure");
            return base.SavingChangesAsync(data, result, cancellationToken);
        }
    }

    private static CardPaymentRequest Card(string key) => new("4111111111111111", "12/30", key);
    private static void Check(bool condition, string message) =>
        ImmediateCheckoutChecks.Check(condition, message);
}
