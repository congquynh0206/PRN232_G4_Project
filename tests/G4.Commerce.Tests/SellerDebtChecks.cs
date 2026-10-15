using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Checkout;
using G4.Infrastructure.Diagnostics;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class SellerDebtChecks
{
    public static async Task RunAsync()
    {
        await CyclesAsync();
        await SalesAndExistingOrdersAsync();
        await PayoutReturnAsync();
        await HeldRevenueAsync();
        await LegacyAsync();
        Console.WriteLine("Seller debt checks passed");
    }
    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    private static async Task Reject<T>(Func<Task> work) where T : Exception
    {
        try { await work(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static async Task CyclesAsync()
    {
        await using var f = await Fixture.Create();
        await f.CreateDebt();
        var first = await f.Finance.GetDebtAsync(2);
        Check(first.Balance == 40m && first.StartedAt == f.Clock.Utc && first.DueAt == f.Clock.Utc.AddSeconds(60), "debt begins on shipping charge and captures deadline");
        var partial = new DebtRepaymentRequest(10m, 40m, 1, "partial");
        var receipt = await f.Finance.RepayDebtAsync(2, partial);
        f.Clock.Advance(20);
        Check((await f.Finance.RepayDebtAsync(2, partial)).Id == receipt.Id && f.Account.NegativeBalance == 30m, "retry returns original receipt without applying twice");
        await Reject<InvalidOperationException>(() => f.Finance.RepayDebtAsync(2, partial with { Amount = 11m }));
        await Reject<ArgumentException>(() => f.Finance.RepayDebtAsync(2, partial with { Key = "precision", Amount = .001m }));
        await Reject<ArgumentException>(() => f.Finance.RepayDebtAsync(2, partial with { Key = "excess", Amount = 41m }));
        await Reject<InvalidOperationException>(() => f.Finance.RepayDebtAsync(2, partial with { Key = "stale" }));
        var rejected = await f.Finance.RepayDebtAsync(2, new(5m, 30m, 1, "rejected", true));
        Check(rejected.Status == "Rejected" && f.Account.NegativeBalance == 30m, "failed bank does not change debt");
        Check((await f.Finance.RepayDebtAsync(2, new(5m, 30m, 1, "rejected", true))).Id == rejected.Id, "failed request replay is stable");
        await f.Finance.ApplyShippingCostAsync(1, 2, 10m);
        Check(f.Account.DebtStartedAt == first.StartedAt && f.Account.DebtDueAt == first.DueAt, "partial repayment and added debt preserve oldest deadline");
        var newConfig = Config(120, 30);
        Check((await new SellerFinanceService(f.Db, newConfig, f.Clock).GetDebtAsync(2)).DueAt == first.DueAt, "policy edits do not extend active debt");
        f.Clock.Advance(24);
        await f.Finance.MaintainDebtsAsync();
        Check(!await f.Db.SellerDebtEvents.AnyAsync(x => x.EventType == "DebtWarning"), "no premature warning");
        f.Clock.Advance(1);
        await f.Finance.MaintainDebtsAsync(); await f.Finance.MaintainDebtsAsync();
        Check(await f.Db.SellerDebtEvents.CountAsync(x => x.EventType == "DebtWarning") == 1, "warning at exact boundary once");
        f.Clock.Advance(15);
        var due = await f.Finance.GetDebtAsync(2);
        Check(due.Overdue && due.Suspended && due.AgeSeconds == 60 && f.Account.Status == "Active", "debt suspension is independent of account status");
        await Reject<InvalidOperationException>(() => f.Finance.ValidateNewSaleAsync(2));
        await Reject<InvalidOperationException>(() => f.Finance.RequestPayoutAsync(2, 1m, "blocked", false));
        f.Account.Status = "FraudRestricted"; await f.Db.SaveChangesAsync();
        await f.Finance.RepayDebtAsync(2, new(40m, 40m, 1, "clear"));
        Check(f.Account.NegativeBalance == 0 && !f.Account.DebtSuspended && f.Account.DebtStartedAt == null && f.Account.Status == "FraudRestricted", "clearing debt keeps other restriction");
        Check(await f.Db.SellerDebtEvents.CountAsync(x => x.EventType == "DebtRestored") == 1, "restoration audit recorded");
        Check(f.Account.AvailableBalance == 0 && await f.Db.FinancialTransactions.CountAsync(x => x.Type == "PlatformFee") == 1, "bank repayment never credits wallet or creates fee");
        var queries = new DiagnosticsQueryService(f.Db);
        Check((await queries.NotificationsAsync(1, "buyer", new(), default)).TotalCount == 0, "buyer cannot see debt notices");
        Check((await queries.NotificationsAsync(3, "seller", new(), default)).TotalCount == 0, "another seller cannot see debt notices");
        Check((await queries.NotificationsAsync(2, "seller", new(EventType: "DebtRestored"), default)).TotalCount == 1, "seller receives own restoration notice without order");
        f.Clock.Advance(1);
        await f.Finance.ApplyShippingCostAsync(1, 3, 40m);
        Check(f.Account.DebtCycleNumber == 2 && f.Account.DebtStartedAt == f.Clock.Utc, "new debt after clearing starts new cycle");
        await Reject<InvalidOperationException>(() => f.Finance.RepayDebtAsync(2, new(10m, 40m, 1, "wrong-cycle")));
        Check((await f.Finance.RepayDebtAsync(2, partial)).Id == receipt.Id && f.Account.NegativeBalance == 40m, "old successful retry cannot affect new cycle");
        receipt = await f.Finance.RepayDebtAsync(2, new(40m, 40m, 2, "clear-second"));
        var saved = await f.Db.SellerDebtRepayments.SingleAsync(x => x.Id == receipt.Id);
        saved.Amount = 1m;
        await Reject<InvalidOperationException>(() => f.Db.SaveChangesAsync());
    }
    private static async Task SalesAndExistingOrdersAsync()
    {
        await using var f = await Fixture.Create(); await f.CreateDebt();
        f.Db.OrderTables.Add(new OrderTable { Id = 2, SellerId = 2, BuyerId = 1, Status = "AwaitingPayment", TotalPrice = 100m,
            OrderDate = f.Clock.Utc.AddSeconds(-1), PaymentExpiresAt = DateTime.UtcNow.AddMinutes(15) });
        await f.Db.SaveChangesAsync(); f.Clock.Advance(60);
        var checkout = new CheckoutService(f.Db, f.Finance, f.Config);
        await Reject<InvalidOperationException>(() => checkout.CreateOrderAsync(1, new(1, new[] { new CartLine(1, 1) }, null, "new-order")));
        Check(await f.Db.OrderTables.CountAsync() == 2 && (await f.Db.Inventories.SingleAsync()).Quantity == 10, "new order blocked before reserving stock even without worker");
        await checkout.PayCardAsync(2, "4111111111111111", "12/30", "old-payment");
        Check(f.Account.NegativeBalance == 0 && f.Account.AvailableBalance == 47.70m && !f.Account.DebtSuspended, "old payable order settles and revenue clears overdue debt");
        await f.Finance.ValidateNewSaleAsync(2);
        Check(await f.Db.SellerDebtEvents.CountAsync(x => x.EventType == "DebtRestored") == 1, "sale restores seller");
    }
    private static async Task PayoutReturnAsync()
    {
        await using var f = await Fixture.Create();
        await f.Finance.RecordSuccessfulPaymentAsync(1, 1);
        var p = await f.Finance.RequestPayoutAsync(2, 87.70m, "returned", true);
        await f.Finance.ApplyShippingCostAsync(1, 1, 40m); f.Clock.Advance(60);
        await f.Finance.GetDebtAsync(2);
        await f.Finance.AdvancePayoutsAsync(); await f.Finance.AdvancePayoutsAsync(); await f.Finance.AdvancePayoutsAsync();
        Check(p.Status == "Returned" && f.Account.NegativeBalance == 0 && f.Account.AvailableBalance == 47.70m && !f.Account.DebtSuspended, "accepted payout continues and returned money clears debt first");
        await f.Finance.AdvancePayoutsAsync();
        Check(f.Account.AvailableBalance == 47.70m, "payout return retry does not credit twice");
    }
    private static async Task HeldRevenueAsync()
    {
        await using var f = await Fixture.Create(); f.Account.Level = 1; await f.Db.SaveChangesAsync();
        await f.Finance.RecordSuccessfulPaymentAsync(1, 1);
        f.Db.OrderTables.Add(new OrderTable { Id = 2, SellerId = 2, BuyerId = 1, Status = "Delivered", TotalPrice = 100m });
        f.Db.Payments.Add(new Payment { Id = 2, OrderId = 2, Amount = 100m, Status = "Succeeded", PaidAt = f.Clock.Utc });
        f.Db.ShippingInfos.Add(new ShippingInfo { OrderId = 2, Direction = "Outbound", Status = "Delivered", DeliveredAt = f.Clock.Utc });
        await f.Db.SaveChangesAsync(); await f.Finance.RecordSuccessfulPaymentAsync(2, 2);
        await f.Finance.ApplyShippingCostAsync(1, 1, 140m);
        Check(f.Account.NegativeBalance == 52.30m, "debt can arise after other revenue starts processing");
        f.Clock.Advance(60); await f.Finance.GetDebtAsync(2); await f.Finance.ReleaseDueFundsAsync();
        Check(f.Account.NegativeBalance == 0 && f.Account.AvailableBalance == 35.40m && !f.Account.DebtSuspended, "release of earlier processing revenue clears later debt");
    }
    private static async Task LegacyAsync()
    {
        await using var f = await Fixture.Create();
        f.Account.NegativeBalance = 50m; f.Account.AvailableBalance = 7m; f.Account.UpdatedAt = f.Clock.Utc.AddYears(-1);
        await f.Db.SaveChangesAsync();
        await f.Finance.MaintainDebtsAsync();
        Check(f.Account.DebtStartedAt == f.Clock.Utc && f.Account.NegativeBalance == 50m && f.Account.AvailableBalance == 7m, "legacy debt gets grace without modifying old balances or using UpdatedAt");
        await f.Finance.MaintainDebtsAsync();
        Check(await f.Db.SellerDebtCycles.CountAsync() == 1, "legacy debt initialization idempotent");
    }
    internal static IConfiguration Config(int due = 60, int warning = 15) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Finance:Debt:DueSeconds"] = due.ToString(), ["Finance:Debt:WarningBeforeSeconds"] = warning.ToString(), ["Finance:Levels:1:HoldSeconds"] = "30" }).Build();
    internal sealed class Clock : TimeProvider
    {
        public DateTime Utc { get; private set; } = new(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Utc);
        public void Advance(int seconds) => Utc = Utc.AddSeconds(seconds);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db = null!;
        public SellerFinanceService Finance = null!;
        public SellerAccount Account = null!;
        public Clock Clock = new(); public IConfiguration Config = SellerDebtChecks.Config();
        public static async Task<Fixture> Create()
        {
            var f = new Fixture { Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options) };
            await f.Db.Database.EnsureCreatedAsync();
            f.Db.Users.AddRange(new User { Id = 1, Role = "buyer" }, new User { Id = 2, Role = "seller", Email = "seller@example.test" });
            f.Account = new SellerAccount { Id = 1, SellerId = 2, Level = 3, MonthlySalesLimit = 1000000m, SalesMonth = new DateTime(2026, 10, 1) };
            f.Db.SellerAccounts.Add(f.Account);
            f.Db.OrderTables.Add(new OrderTable { Id = 1, SellerId = 2, BuyerId = 1, Status = "Paid", TotalPrice = 100m, OrderDate = f.Clock.Utc });
            f.Db.Payments.Add(new Payment { Id = 1, OrderId = 1, Status = "Succeeded", Amount = 100m, PaidAt = f.Clock.Utc });
            f.Db.Products.Add(new Product { Id = 1, SellerId = 2, Price = 100m, WeightKg = 1m });
            f.Db.Inventories.Add(new Inventory { ProductId = 1, Quantity = 10 });
            f.Db.Addresses.AddRange(new Address { Id = 1, UserId = 1, State = "Hanoi", Country = "Vietnam" },
                new Address { Id = 2, UserId = 2, IsDefault = true, State = "Hanoi", Country = "Vietnam" });
            await f.Db.SaveChangesAsync(); f.Finance = new(f.Db, f.Config, f.Clock); return f;
        }
        public async Task CreateDebt()
        {
            await Finance.RecordSuccessfulPaymentAsync(1, 1);
            await Finance.RequestPayoutAsync(2, 87.70m, "initial", false);
            await Finance.ApplyShippingCostAsync(1, 1, 40m);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
