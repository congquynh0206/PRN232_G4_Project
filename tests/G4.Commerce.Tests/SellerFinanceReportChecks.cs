using G4.Domain.Entities;
using G4.Infrastructure.Finance;
using Microsoft.EntityFrameworkCore;

internal static class SellerFinanceReportChecks
{
    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var paid = new DateTime(2026, 10, 18, 18, 0, 0, DateTimeKind.Utc); // 19 Oct in Vietnam
        var refunded = paid.AddDays(1);
        db.OrderTables.AddRange(new OrderTable { Id = 101, SellerId = 1 }, new OrderTable { Id = 102, SellerId = 2 },
            new OrderTable { Id = 103, SellerId = 1 });
        db.SellerAccounts.AddRange(new SellerAccount { Id = 1, SellerId = 1 }, new SellerAccount { Id = 2, SellerId = 2 });
        db.Payments.AddRange(new Payment { Id = 11, OrderId = 101, Status = "Succeeded", PaidAt = paid, Amount = 100m },
            new Payment { Id = 12, OrderId = 102, Status = "Succeeded", PaidAt = paid, Amount = 900m },
            new Payment { Id = 13, OrderId = 103, Status = "Failed", Amount = 999m },
            new Payment { Id = 14, OrderId = 103, Status = "Succeeded", PaidAt = paid, Amount = 4m });
        db.SellerSettlements.AddRange(new SellerSettlement { Id = 21, SellerId = 1, SellerAccountId = 1, PaymentId = 11, OrderId = 101 },
            new SellerSettlement { Id = 22, SellerId = 2, SellerAccountId = 2, PaymentId = 12, OrderId = 102 });
        db.Refunds.AddRange(new Refund { Id = 31, OrderId = 101, PaymentId = 11, Status = "Succeeded", CompletedAt = refunded, Amount = 20m },
            new Refund { Id = 32, OrderId = 101, PaymentId = 11, Status = "Failed", CompletedAt = refunded, Amount = 30m });
        void Entry(long id, string type, decimal amount, int sellerAccount = 1, int? settlement = 21, int? refund = null, DateTime? at = null, string key = "") =>
            db.FinancialTransactions.Add(new FinancialTransaction { Id = id, SellerAccountId = sellerAccount, SettlementId = settlement,
                OrderId = sellerAccount == 1 ? 101 : 102, RefundId = refund, Type = type, Amount = amount,
                EntryKey = key, CreatedAt = at ?? refunded.AddDays(1) });
        Entry(41, "Sale", 100m); Entry(42, "PlatformFee", -12.30m);
        Entry(43, "Refund", -20m, refund: 31); Entry(44, "FeeCredit", 2.46m, refund: 31);
        Entry(45, "RefundApplied", 0m, refund: 31);
        Entry(46, "Sale", 900m, 2, 22);
        Entry(47, "Payout", -50m); Entry(48, "DebtRecovery", -7m); Entry(49, "FundReleased", 87.70m);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new SellerFinanceReportService(db, TimeProvider.System);
        var start = new DateOnly(2026, 10, 19); var end = new DateOnly(2026, 10, 20);
        var report = await service.GetAsync(1, start, end, "day", "Asia/Ho_Chi_Minh", 1, 1);
        Check(report.Totals.BuyerPaid == 100m && report.Totals.BuyerRefunded == 20m
            && report.Totals.PlatformFee == 9.84m && report.Totals.Net == 70.16m && report.Totals.PaidOrders == 1,
            "paid/refund/fee/net/count, no other seller or wallet transfers");
        Check(report.Chart.Count == 2 && report.Chart[0].Net == 87.70m && report.Chart[1].Net == -17.54m,
            "business timestamps place delayed ledger entries in correct local days; refund-only net negative");
        Check(report.Items.Count == 1 && report.Items[0].Sources?.Count == 2 && report.TotalCount == 2,
            "page only limits table and contains source entries");
        Check(report.UnreconciledPayments == 1 && report.UnreconciledRefunds == 0,
            "successful payment without settlement is flagged, not fabricated into totals");
        var page2 = await service.GetAsync(1, start, end, "day", "Asia/Ho_Chi_Minh", 2, 1);
        Check(page2.Totals == report.Totals && page2.Items[0].Net == -17.54m, "paging cannot change totals");
        var week = await service.GetAsync(1, start, end, "week", "Asia/Ho_Chi_Minh");
        Check(week.Chart.Count == 1 && week.Chart[0].Period == new DateOnly(2026, 10, 19), "Monday-based grouping");
        await Reject(() => service.GetAsync(1, end, start, "day", "UTC"));
        await Reject(() => service.GetAsync(1, start, end, "hour", "UTC"));
        await Reject(() => service.GetAsync(1, start, end, "day", "not/a-zone"));
        Check(!db.ChangeTracker.HasChanges(), "report is read-only");
        await Reject(() => service.GetAsync(1, DateOnly.MinValue, DateOnly.MinValue, "week", "UTC"));
        await Reject(() => service.GetAsync(1, DateOnly.MaxValue, DateOnly.MaxValue, "day", "UTC"));
        var empty = await service.GetAsync(1, start.AddMonths(-1), start.AddMonths(-1), "day", "Asia/Ho_Chi_Minh");
        Check(empty.Chart.Count == 1 && empty.Totals.Net == 0m && empty.Items[0].Sources?.Count == 0, "empty day is represented as zero without invented sources");
        // Subsidy is seller funding, not buyer cash; return postage and its buyer recovery must not double count.
        var later = refunded.AddDays(1);
        db.OrderTables.Add(new OrderTable { Id = 104, SellerId = 1 });
        db.Payments.Add(new Payment { Id = 15, OrderId = 104, Status = "Succeeded", PaidAt = later, Amount = 80m });
        db.SellerSettlements.Add(new SellerSettlement { Id = 24, SellerId = 1, SellerAccountId = 1, OrderId = 104, PaymentId = 15 });
        db.Refunds.Add(new Refund { Id = 34, OrderId = 104, PaymentId = 15, Status = "Succeeded", CompletedAt = later,
            Amount = 17m, BuyerReturnShippingRecovery = 3m, PlatformSubsidyReversalAmount = 5m });
        db.FinancialTransactions.AddRange(
            new FinancialTransaction { Id = 50, SellerAccountId = 1, SettlementId = 24, OrderId = 104, Type = "Sale", Amount = 80m },
            new FinancialTransaction { Id = 51, SellerAccountId = 1, SettlementId = 24, OrderId = 104, Type = "PlatformSubsidy", Amount = 20m },
            new FinancialTransaction { Id = 52, SellerAccountId = 1, SettlementId = 24, OrderId = 104, Type = "PlatformFee", Amount = -12.30m },
            new FinancialTransaction { Id = 53, SellerAccountId = 1, SettlementId = 24, OrderId = 104, RefundId = 34, Type = "Refund", Amount = -17m },
            new FinancialTransaction { Id = 54, SellerAccountId = 1, SettlementId = 24, OrderId = 104, RefundId = 34, Type = "PlatformSubsidyReversal", Amount = -5m },
            new FinancialTransaction { Id = 55, SellerAccountId = 1, SettlementId = 24, OrderId = 104, RefundId = 34, Type = "FeeCredit", Amount = 2.46m },
            new FinancialTransaction { Id = 56, SellerAccountId = 1, OrderId = 104, Type = "ReturnShippingCost", Amount = -3m, CreatedAt = later },
            new FinancialTransaction { Id = 57, SellerAccountId = 1, OrderId = 104, Type = "ShippingAdjustmentCost", Amount = -1m, CreatedAt = later },
            new FinancialTransaction { Id = 58, SellerAccountId = 1, OrderId = 104, Type = "ShippingAdjustmentCost", Amount = -2m, CreatedAt = later },
            new FinancialTransaction { Id = 59, SellerAccountId = 1, OrderId = 104, Type = "NegativeBalance", Amount = -1m,
                EntryKey = "address-change:1:cost:negative", CreatedAt = later },
            new FinancialTransaction { Id = 60, SellerAccountId = 1, OrderId = 104, RefundId = 34, Type = "RefundApplied", Amount = 0m });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var complex = await service.GetAsync(1, new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 21), "day", "Asia/Ho_Chi_Minh");
        Check(complex.Totals.BuyerPaid == 80m && complex.Totals.PlatformSubsidy == 20m
            && complex.Totals.BuyerRefunded == 17m && complex.Totals.SubsidyReversal == 5m
            && complex.Totals.ReturnShippingRecovery == 3m && complex.Totals.ReturnShippingCost == 3m
            && complex.Totals.ShippingAdjustments == 4m && complex.Totals.Net == 61.16m,
            "subsidy, return shipping recovery and split/debt shipping adjustment reconcile to ledger once");        Console.WriteLine("Seller report: paid/refund/fee/net, timezone, backfill, negative period, seller isolation, warning, paging passed");
    }
    private static void Check(bool ok, string detail) { if (!ok) throw new Exception(detail); }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); } catch (ArgumentException) { return; }
        throw new Exception("Invalid report filter accepted");
    }
}
