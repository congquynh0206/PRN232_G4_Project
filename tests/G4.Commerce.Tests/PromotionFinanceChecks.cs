using G4.Application.Integrations;
using G4.Domain.Entities;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Persistence;
using G4.Infrastructure.Returns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class PromotionFinanceChecks
{
    public static async Task RunAsync()
    {
        var checks = new (string Name, Func<Task> Check)[]
        {
            ("platform funding settles and fully reverses once", FundingAndFullRefundAsync),
            ("monthly limit includes subsidy", MonthlyLimitAsync),
            ("partial refunds reverse cumulative cents", CumulativeRefundAsync),
            ("partial refunds consume the remaining order hold", PartialHeldRefundAsync),
            ("legacy finance keeps buyer payment policy", LegacyAsync),
            ("zero promotion payment refunds internally", ZeroRefundAsync),
            ("fully seller-funded zero payment settles without fee", ZeroSellerFundedAsync),
            ("legacy and external zero payments are rejected", RejectInvalidZeroAsync),
            ("mismatched payment and refund cannot affect another order", RejectForeignPaymentAsync)
        };
        var failures = new List<string>();
        foreach (var (name, check) in checks)
        {
            try { await check(); }
            catch (Exception ex) { failures.Add($"{name}: {ex.Message}"); }
        }
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
        Console.WriteLine($"Promotion finance: {checks.Length} funding, cumulative refund and zero-payment checks passed");
    }

    private static async Task FundingAndFullRefundAsync()
    {
        await using var db = Database(); await SeedAsync(db, 90, 5, 1);
        var finance = Finance(db);
        var settlement = await finance.RecordSuccessfulPaymentAsync(1, 1);
        Equal(95m, settlement.GrossAmount); Equal(11.70m, settlement.PlatformFeeAmount); Equal(83.30m, settlement.NetAmount);
        Equal(95m, (await db.SellerAccounts.SingleAsync()).MonthlySalesAmount);
        await finance.RecordSuccessfulPaymentAsync(1, 1);
        Equal(5m, (await db.FinancialTransactions.SingleAsync(x => x.Type == "PlatformSubsidy")).Amount);
        Equal(90m, (await db.FinancialTransactions.SingleAsync(x => x.Type == "Sale")).Amount);
        await finance.PlaceHoldAsync(1, "return");
        var refund = await AddRefundAsync(db, 90);
        await finance.ApplyRefundAsync(1, refund.Id); await finance.ApplyRefundAsync(1, refund.Id);
        Equal(95m, settlement.RefundedAmount); Equal(11.70m, settlement.FeeCreditAmount); Equal("Refunded", settlement.Status);
        Equal(-5m, (await db.FinancialTransactions.SingleAsync(x => x.Type == "PlatformSubsidyReversal")).Amount);
        Equal(-90m, (await db.FinancialTransactions.SingleAsync(x => x.Type == "Refund")).Amount);
        var account = await db.SellerAccounts.SingleAsync();
        Equal(0m, account.OnHoldBalance); Equal(0m, account.ProcessingBalance); Equal(0m, account.NegativeBalance);
    }
    private static async Task MonthlyLimitAsync()
    {
        await using var db = Database(); await SeedAsync(db, 90, 5, 1);
        db.SellerAccounts.Add(new SellerAccount { SellerId = 2, MonthlySalesLimit = 92, SalesMonth = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await RejectAsync(() => Finance(db).ValidateMonthlyLimitAsync(1));
        await RejectAsync(() => Finance(db).RecordSuccessfulPaymentAsync(1, 1));
        Equal(0, await db.SellerSettlements.CountAsync());
    }
    private static async Task CumulativeRefundAsync()
    {
        await using var db = Database(); await SeedAsync(db, 3, 1, 1);
        var finance = Finance(db); var settlement = await finance.RecordSuccessfulPaymentAsync(1, 1);
        var first = await AddRefundAsync(db, 1); await finance.ApplyRefundAsync(1, first.Id);
        Equal(1.33m, settlement.RefundedAmount);
        Equal(-.33m, (await db.FinancialTransactions.SingleAsync(x => x.RefundId == first.Id && x.Type == "PlatformSubsidyReversal")).Amount);
        var second = await AddRefundAsync(db, 1); await finance.ApplyRefundAsync(1, second.Id);
        Equal(2.67m, settlement.RefundedAmount);
        Equal(-.34m, (await db.FinancialTransactions.SingleAsync(x => x.RefundId == second.Id && x.Type == "PlatformSubsidyReversal")).Amount);
        var third = await AddRefundAsync(db, 1); await finance.ApplyRefundAsync(1, third.Id);
        Equal(4m, settlement.RefundedAmount); Equal(.78m, settlement.FeeCreditAmount);
        Equal(-1m, await db.FinancialTransactions.Where(x => x.Type == "PlatformSubsidyReversal").SumAsync(x => x.Amount));
        var fourth = await AddRefundAsync(db, .01m); await RejectAsync(() => finance.ApplyRefundAsync(1, fourth.Id));
    }
    private static async Task LegacyAsync()
    {
        await using var db = Database(); await SeedAsync(db, 90, 5, 0);
        var finance = Finance(db); var settlement = await finance.RecordSuccessfulPaymentAsync(1, 1);
        Equal(90m, settlement.GrossAmount); Equal(11.10m, settlement.PlatformFeeAmount);
        var refund = await AddRefundAsync(db, 90); await finance.ApplyRefundAsync(1, refund.Id);
        Equal(90m, settlement.RefundedAmount); Equal(0, await db.FinancialTransactions.CountAsync(x => x.Type == "PlatformSubsidy" || x.Type == "PlatformSubsidyReversal"));
    }
    private static async Task PartialHeldRefundAsync()
    {
        await using var db = Database(); await SeedAsync(db, 3, 1, 1);
        var finance = Finance(db); await finance.RecordSuccessfulPaymentAsync(1, 1);
        await finance.PlaceHoldAsync(1, "return");
        for (var i = 0; i < 3; i++)
        {
            var refund = await AddRefundAsync(db, 1); await finance.ApplyRefundAsync(1, refund.Id);
        }
        var account = await db.SellerAccounts.SingleAsync();
        Equal(0m, account.NegativeBalance); Equal(0m, account.OnHoldBalance);
    }
    private static async Task ZeroRefundAsync()
    {
        await using var db = Database(); await SeedAsync(db, 0, 10, 1, "Promotion");
        var finance = Finance(db); var gateway = new RefundGateway();
        var returns = new ReturnService(db, gateway, new Carrier(), finance);
        var settlement = await finance.RecordSuccessfulPaymentAsync(1, 1);
        var refund = await returns.RefundAsync(1, "cancel", null);
        Equal("Succeeded", refund.Status); Equal(0m, refund.Amount); Equal(0, gateway.Calls);
        await returns.RefundAsync(1, "cancel", null);
        Equal(1, await db.Refunds.CountAsync()); Equal(10m, settlement.RefundedAmount); Equal(1.50m, settlement.FeeCreditAmount);
        Equal(-10m, (await db.FinancialTransactions.SingleAsync(x => x.Type == "PlatformSubsidyReversal")).Amount);
        Equal(0m, (await db.SellerAccounts.SingleAsync()).ProcessingBalance);
        await RejectAsync(() => returns.RefundAsync(1, "dispute", null));
    }
    private static async Task ZeroSellerFundedAsync()
    {
        await using var db = Database(); await SeedAsync(db, 0, 0, 1, "Promotion");
        var finance = Finance(db); var settlement = await finance.RecordSuccessfulPaymentAsync(1, 1);
        Equal(0m, settlement.NetAmount); Equal(0m, settlement.PlatformFeeAmount);
        var gateway = new RefundGateway();
        var refund = await new ReturnService(db, gateway, new Carrier(), finance).RefundAsync(1, "cancel", null);
        Equal("Succeeded", refund.Status); Equal("Refunded", settlement.Status); Equal(0, gateway.Calls);
    }
    private static async Task RejectInvalidZeroAsync()
    {
        foreach (var (schema, method) in new[] { (0, "Promotion"), (1, "Card") })
        {
            await using var db = Database(); await SeedAsync(db, 0, 0, schema, method);
            var gateway = new RefundGateway();
            await RejectAsync(() => new ReturnService(db, gateway, new Carrier()).RefundAsync(1, "cancel", null));
            Equal(0, gateway.Calls); Equal(0, await db.Refunds.CountAsync());
        }
    }
    private static async Task RejectForeignPaymentAsync()
    {
        await using var db = Database(); await SeedAsync(db, 90, 5, 1);
        db.Payments.Add(new Payment { Id = 2, OrderId = 2, Amount = 90, Status = "Succeeded", Method = "Card" });
        await db.SaveChangesAsync(); await RejectAsync(() => Finance(db).RecordSuccessfulPaymentAsync(1, 2));
    }
    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static SellerFinanceService Finance(ApplicationDbContext db) => new(db, new ConfigurationBuilder().Build());
    private static async Task SeedAsync(ApplicationDbContext db, decimal buyer, decimal subsidy, int schema, string method = "Card")
    {
        db.AddRange(new User { Id = 1, Role = "buyer" }, new User { Id = 2, Role = "seller" },
            new OrderTable { Id = 1, BuyerId = 1, SellerId = 2, Status = "Cancelled", TotalPrice = buyer, PricingSchemaVersion = schema, PlatformSubsidy = subsidy, SellerGrossSnapshot = buyer + subsidy },
            new Payment { Id = 1, OrderId = 1, UserId = 1, Amount = buyer, Status = "Succeeded", Method = method });
        await db.SaveChangesAsync();
    }
    private static async Task<Refund> AddRefundAsync(ApplicationDbContext db, decimal amount)
    {
        var refund = new Refund { OrderId = 1, PaymentId = 1, Amount = amount, Status = "Succeeded", Reason = "test", IdempotencyKey = Guid.NewGuid().ToString() };
        db.Refunds.Add(refund); await db.SaveChangesAsync(); return refund;
    }
    private static async Task RejectAsync(Func<Task> action)
    {
        try { await action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Unsafe finance operation was accepted");
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}");
    }
    private sealed class RefundGateway : IRefundGateway
    {
        public int Calls { get; private set; }
        public Task<string> RefundAsync(Payment payment, decimal amount, string key, CancellationToken ct) { Calls++; return Task.FromResult(key); }
    }
    private sealed class Carrier : ICarrierGateway
    {
        public Task<string> CreateLabelAsync(int orderId, string direction, string key, CancellationToken ct) => Task.FromResult("TEST");
    }
}
