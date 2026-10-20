using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class PayoutWorkflowChecks
{
    public static async Task RunAsync()
    {
        foreach (var level in new[] { 1, 2, 3 }) await Flow(level);
        await ReturnedDebtAndHistory(); await LegacyAndTotals();
        Console.WriteLine("PAY1: all levels follow bank workflow, wallet debit/pending/paid-out totals, terminal-only notifications, actor history, retry, returned funds/debt recovery and legacy preservation passed");
    }
    private static async Task<(ApplicationDbContext Db, SellerFinanceService Finance, SellerAccount Account)> Fixture(int level)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Users.Add(new User { Id = 10, Role = "seller", Email = "payout@example.test" });
        var account = new SellerAccount { SellerId = 10, Level = level, AvailableBalance = 87.70m };
        db.SellerAccounts.Add(account); await db.SaveChangesAsync();
        return (db, new SellerFinanceService(db, new ConfigurationBuilder().Build()), account);
    }
    private static async Task Flow(int level)
    {
        var f = await Fixture(level); await using var db = f.Db;
        var p = await f.Finance.RequestPayoutAsync(10, 50m, "withdraw", false);
        Check(p.Status == "Created" && p.CompletedAt == null && p.BankReferenceId == null && f.Account.AvailableBalance == 37.70m, "Level " + level + " reserves withdrawal without inventing completion");
        Check((await f.Finance.RequestPayoutAsync(10, 50m, "withdraw", false)).Id == p.Id, "request replay returns same payout");
        await Reject(() => f.Finance.RequestPayoutAsync(10, 51m, "withdraw", false));
        await Reject(() => f.Finance.RequestPayoutAsync(10, 50m, "another", false));
        var summary = await f.Finance.GetSummaryAsync(10);
        Check(summary.PayoutPending == 50m && summary.PaidOut == 0 && summary.Available == 37.70m, "pending total is independent of spendable wallet");
        Check(!await db.NotificationOutbox.AnyAsync(x => x.PayoutId == p.Id), "pending has no completion email");
        foreach (var status in new[] { "InProgress", "FundsSent", "Completed" })
        {
            await f.Finance.AdvancePayoutsAsync(); Check(p.Status == status, "worker advances exactly one bank step " + status);
            if (status != "Completed") Check(p.CompletedAt == null && !await db.NotificationOutbox.AnyAsync(x => x.PayoutId == p.Id), "bank pending is not completion");
        }
        var steps = await db.SellerPayoutEvents.Where(e => e.PayoutId == p.Id).OrderBy(e => e.Id).ToListAsync();
        Check(steps.Select(e => e.ToStatus).SequenceEqual(new[] { "Created", "InProgress", "FundsSent", "Completed" })
            && steps[0].ActorUserId == 10 && steps[0].ActorRole == "seller" && steps.Skip(1).All(e => e.ActorRole == "system" && e.ActorUserId == null), "history records real actors and every transition");
        Check(await db.NotificationOutbox.CountAsync(x => x.PayoutId == p.Id && x.EventType == "PayoutCompleted") == 1, "one final completion email");
        await f.Finance.AdvancePayoutsAsync(); await f.Finance.AdvancePayoutsAsync();
        Check(await db.SellerPayoutEvents.CountAsync(x => x.PayoutId == p.Id) == 4 && f.Account.AvailableBalance == 37.70m, "terminal workers cannot repeat money or history");
        summary = await f.Finance.GetSummaryAsync(10);
        Check(summary.PayoutPending == 0 && summary.PaidOut == 50m && summary.Payouts.Single().History!.Count == 4, "summary shows completed amount and UTC history");
        Check(summary.Payouts.Single().History!.All(e => e.OccurredAt.Kind == DateTimeKind.Utc), "history DTO is UTC");
        Check(await db.CommerceChanges.AnyAsync(x => x.Topic == "finance" && x.SellerAccountId == f.Account.Id), "bank steps invalidate finance UI");
    }
    private static async Task ReturnedDebtAndHistory()
    {
        var f = await Fixture(3); await using var db = f.Db;
        var p = await f.Finance.RequestPayoutAsync(10, 50m, "returned", true);
        f.Account.AvailableBalance = 0; f.Account.NegativeBalance = 20m; await db.SaveChangesAsync();
        for (var i = 0; i < 3; i++) await f.Finance.AdvancePayoutsAsync();
        Check(p.Status == "Returned" && f.Account.NegativeBalance == 0 && f.Account.AvailableBalance == 30m, "returned amount recovers debt before remainder becomes available");
        await f.Finance.AdvancePayoutsAsync();
        Check(f.Account.AvailableBalance == 30m && await db.FinancialTransactions.CountAsync(x => x.PayoutId == p.Id && x.Type == "PayoutReturned") == 1, "returned amount restored once");
        var step = await db.SellerPayoutEvents.FirstAsync(e => e.PayoutId == p.Id);
        step.ToStatus = "Tampered";
        await Reject(() => db.SaveChangesAsync()); db.Entry(step).State = EntityState.Unchanged;
        db.SellerPayoutEvents.Remove(step); await Reject(() => db.SaveChangesAsync()); db.Entry(step).State = EntityState.Unchanged;
        Check(await db.NotificationOutbox.CountAsync(x => x.PayoutId == p.Id && x.EventType == "PayoutReturned") == 1, "returned mail is emitted once after money restoration");
    }
    private static async Task LegacyAndTotals()
    {
        var f = await Fixture(3); await using var db = f.Db;
        for (var i = 0; i < 25; i++) db.SellerPayouts.Add(new SellerPayout { SellerAccountId = f.Account.Id, Amount = 1m,
            Status = "Completed", IdempotencyKey = "old-" + i, CreatedAt = DateTime.UtcNow.AddDays(-1), CompletedAt = DateTime.UtcNow.AddDays(-1) });
        db.SellerPayouts.Add(new SellerPayout { SellerAccountId = f.Account.Id, Amount = 5m, Status = "Returned", IdempotencyKey = "old-returned", CreatedAt = DateTime.UtcNow.AddDays(-1), CompletedAt = DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync(); await f.Finance.AdvancePayoutsAsync();
        var summary = await f.Finance.GetSummaryAsync(10);
        Check(summary.Payouts.Count == 20 && summary.PaidOut == 25m && summary.PayoutPending == 0 && !await db.SellerPayoutEvents.AnyAsync(), "legacy completed is untouched and totals are not limited by page size");
        var pending = new SellerPayout { SellerAccountId = f.Account.Id, Amount = 3m, Status = "FundsSent", BankReferenceId = "LEGACY-PENDING", IdempotencyKey = "old-pending", CreatedAt = DateTime.UtcNow.AddDays(-1) };
        db.SellerPayouts.Add(pending); await db.SaveChangesAsync(); await f.Finance.AdvancePayoutsAsync();
        var step = await db.SellerPayoutEvents.SingleAsync(e => e.PayoutId == pending.Id);
        Check(pending.Status == "Completed" && f.Account.AvailableBalance == 87.70m && step.FromStatus == "FundsSent" && step.ToStatus == "Completed"
            && step.ActorRole == "system", "legacy pending continues from actual state without redebiting or fabricating earlier history");
    }
    internal static void Check(bool value, string message) => ReturnAutomationChecks.Check(value, message);
    internal static Task Reject(Func<Task> work) => ShipmentReattemptChecks.Reject<InvalidOperationException>(work);
}
