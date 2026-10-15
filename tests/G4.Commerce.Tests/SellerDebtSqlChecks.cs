using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.RegularExpressions;

internal static class SellerDebtSqlChecks
{
    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("G4_DEBT_SQL_TEST_CONNECTION") ?? throw new Exception("Debt SQL QA connection required");
        if (new SqlConnectionStringBuilder(connection).InitialCatalog != "G4_SellerDebt_Test_20261014")
            throw new Exception("Dedicated debt QA database required");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        await Bootstrap();
        var config = SellerDebtChecks.Config(); var clock = new SellerDebtChecks.Clock();
        int sellerId, buyerId, accountId;
        await using (var db = new ApplicationDbContext(options))
        {
            var seller = new User { Role = "seller", Email = $"debt-seller-{Guid.NewGuid():N}@example.test" };
            var buyer = new User { Role = "buyer", Email = $"debt-buyer-{Guid.NewGuid():N}@example.test" };
            db.Users.AddRange(seller, buyer); await db.SaveChangesAsync();
            (sellerId, buyerId) = (seller.Id, buyer.Id);
            var account = new SellerAccount { SellerId = sellerId, NegativeBalance = 100m, Level = 3, MonthlySalesLimit = 1000000m,
                SalesMonth = new DateTime(clock.Utc.Year, clock.Utc.Month, 1) };
            db.SellerAccounts.Add(account); await db.SaveChangesAsync(); accountId = account.Id;
            await new SellerFinanceService(db, config, clock).GetDebtAsync(sellerId);
        }
        async Task<DebtRepaymentView> Repay(DebtRepaymentRequest r)
        {
            await using var db = new ApplicationDbContext(options);
            return await new SellerFinanceService(db, config, clock).RepayDebtAsync(sellerId, r);
        }
        var request = new DebtRepaymentRequest(70m, 100m, 1, "duplicate");
        var same = await Task.WhenAll(Repay(request), Repay(request));
        Check(same[0].Id == same[1].Id, "concurrent identical repayment returns one receipt");
        await using (var db = new ApplicationDbContext(options))
        {
            Check((await db.SellerAccounts.SingleAsync(a => a.Id == accountId)).NegativeBalance == 30m, "concurrent duplicate only reduces debt once");
            Check(await db.SellerDebtRepayments.CountAsync(x => x.SellerAccountId == accountId) == 1
                && await db.FinancialTransactions.CountAsync(x => x.SellerAccountId == accountId && x.Type == "DebtRepayment") == 1, "one receipt and ledger entry committed");
        }
        async Task<bool> TryRepay(DebtRepaymentRequest r)
        {
            try { await Repay(r); return true; } catch (InvalidOperationException) { return false; }
        }
        var different = await Task.WhenAll(TryRepay(new(20m, 30m, 1, "race-a")), TryRepay(new(20m, 30m, 1, "race-b")));
        Check(different.Count(x => x) == 1, "different simultaneous requests serialize; stale snapshot is rejected");
        clock.Advance(60);
        async Task Maintain()
        {
            await using var db = new ApplicationDbContext(options);
            await new SellerFinanceService(db, config, clock).GetDebtAsync(sellerId);
        }
        await Task.WhenAll(Maintain(), Maintain());
        int orderId, paymentId;
        await using (var db = new ApplicationDbContext(options))
        {
            Check(await db.SellerDebtEvents.CountAsync(x => x.SellerAccountId == accountId && x.EventType == "DebtOverdue") == 1, "concurrent deadline checks suspend once");
            var order = new OrderTable { SellerId = sellerId, BuyerId = buyerId, Status = "Paid", TotalPrice = 100m, OrderDate = clock.Utc.AddSeconds(-90) };
            db.OrderTables.Add(order); await db.SaveChangesAsync(); orderId = order.Id;
            var payment = new Payment { OrderId = orderId, UserId = buyerId, Status = "Succeeded", Amount = 100m, PaidAt = clock.Utc };
            db.Payments.Add(payment); await db.SaveChangesAsync(); paymentId = payment.Id;
        }
        async Task Sale()
        {
            await using var db = new ApplicationDbContext(options);
            await new SellerFinanceService(db, config, clock).RecordSuccessfulPaymentAsync(orderId, paymentId);
        }
        var repaymentTask = TryRepay(new(10m, 10m, 1, "sale-race"));
        await Task.WhenAll(repaymentTask, Sale());
        await using (var db = new ApplicationDbContext(options))
        {
            var a = await db.SellerAccounts.SingleAsync(x => x.Id == accountId);
            Check(a.NegativeBalance == 0 && !a.DebtSuspended && a.AvailableBalance == (repaymentTask.Result ? 87.70m : 77.70m), "concurrent sale/repayment conserves money and clears restrictions");
            Check(await db.SellerDebtEvents.CountAsync(x => x.SellerAccountId == accountId && x.EventType == "DebtRestored") == 1, "concurrent clearing restores once");
            Check(await db.NotificationOutbox.CountAsync(x => x.SellerId == sellerId && x.EventType == "DebtRestored") == 1, "notification outbox survives concurrency with one restoration");
        }
        async Task<SellerPayout?> Payout(decimal amount, string key, bool fail)
        {
            await using var db = new ApplicationDbContext(options);
            try { return await new SellerFinanceService(db, config, clock).RequestPayoutAsync(sellerId, amount, key, fail); }
            catch (InvalidOperationException) { return null; }
        }
        var payoutKey = $"debt-qa:{sellerId}:same";
        var payoutRetries = await Task.WhenAll(Payout(10m, payoutKey, true), Payout(10m, payoutKey, true));
        Check(payoutRetries[0] != null && payoutRetries[0]!.Id == payoutRetries[1]?.Id, "SQL payout retries reserve funds once");
        var competingPayouts = await Task.WhenAll(Payout(50m, $"debt-qa:{sellerId}:a", false), Payout(50m, $"debt-qa:{sellerId}:b", false));
        Check(competingPayouts.Count(x => x != null) == 1, "concurrent payouts cannot overspend wallet");
        await using (var db = new ApplicationDbContext(options))
            await new SellerFinanceService(db, config, clock).ApplyShippingCostAsync(orderId, sellerId, 40m);
        async Task AdvancePayouts()
        {
            await using var db = new ApplicationDbContext(options);
            await new SellerFinanceService(db, config, clock).AdvancePayoutsAsync();
        }
        await Task.WhenAll(AdvancePayouts(), AdvancePayouts());
        await Task.WhenAll(AdvancePayouts(), AdvancePayouts());
        await using (var db = new ApplicationDbContext(options))
        {
            var a = await db.SellerAccounts.SingleAsync(x => x.Id == accountId);
            Check(a.AvailableBalance == 0 && a.NegativeBalance == (repaymentTask.Result ? 2.30m : 12.30m), "bank return recovers debt under concurrent payout workers");
            Check(await db.FinancialTransactions.CountAsync(x => x.SellerAccountId == accountId && x.Type == "PayoutReturned") == 1,
                "concurrent payout workers only return funds once");
        }
        var independentSellers = new List<int>();
        await using (var db = new ApplicationDbContext(options))
            for (var n = 0; n < 2; n++)
            {
                var user = new User { Role = "seller", Email = $"parallel-{Guid.NewGuid():N}@example.test" };
                db.Users.Add(user); await db.SaveChangesAsync();
                db.SellerAccounts.Add(new SellerAccount { SellerId = user.Id, NegativeBalance = 10m,
                    MonthlySalesLimit = 5000m, SalesMonth = new DateTime(clock.Utc.Year, clock.Utc.Month, 1) });
                await db.SaveChangesAsync();
                await new SellerFinanceService(db, config, clock).GetDebtAsync(user.Id);
                independentSellers.Add(user.Id);
            }
        await Task.WhenAll(independentSellers.Select(async id =>
        {
            await using var db = new ApplicationDbContext(options);
            var r = await new SellerFinanceService(db, config, clock).RepayDebtAsync(id, new(10m, 10m, 1, "independent"));
            Check(r.Status == "Succeeded", "independent sellers repay without key-range deadlocks");
        }));
        Console.WriteLine("SQL debt checks passed: migration preservation, concurrent repayment/payout retries, independent sellers, stale debt, deadline, sale recovery, bank return and notifications");

        async Task Bootstrap()
        {
            await using var db = new ApplicationDbContext(options);
            var creator = db.Database.GetService<IRelationalDatabaseCreator>();
            if (!await creator.ExistsAsync()) await creator.CreateAsync();
            await using var sql = new SqlConnection(connection); await sql.OpenAsync();
            await using var exists = new SqlCommand("SELECT OBJECT_ID(N'[User]')", sql);
            var fresh = await exists.ExecuteScalarAsync() is DBNull or null;
            if (fresh)
            {
                var baseline = Regex.Replace(await File.ReadAllTextAsync("database/baseline.sql"), @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE CloneEbayDB;)\s*$", "");
                foreach (var batch in Regex.Split(baseline, @"(?im)^\s*GO\s*$"))
                    if (!string.IsNullOrWhiteSpace(batch)) { await using var cmd = new SqlCommand(batch, sql); await cmd.ExecuteNonQueryAsync(); }
                await db.Database.GetService<IMigrator>().MigrateAsync("20261014091619_AddAdminDisputeDecisions");
                await using var legacy = new SqlCommand(@"
                    INSERT INTO [User] ([role], [email]) VALUES (N'seller', N'legacy-debt@example.test');
                    INSERT INTO [SellerAccount] (SellerId, Level, Status, MonthlySalesLimit, HoldDays, ProcessingBalance,
                        AvailableBalance, OnHoldBalance, NegativeBalance, MonthlySalesAmount, SalesMonth, UpdatedAt)
                    VALUES (SCOPE_IDENTITY(), 1, N'ManualRestricted', 5000, 21, 23, 7, 5, 50, 100,
                        '2026-10-01', '2025-01-01');", sql);
                await legacy.ExecuteNonQueryAsync();
            }
            await db.Database.MigrateAsync();
            var old = await db.SellerAccounts.AsNoTracking().FirstOrDefaultAsync(a => db.Users.Any(u => u.Id == a.SellerId && u.Email == "legacy-debt@example.test"));
            if (old != null) Check(old.NegativeBalance == 50m && old.AvailableBalance == 7m && old.ProcessingBalance == 23m
                && old.OnHoldBalance == 5m && old.Status == "ManualRestricted", "migration preserves legacy balances and status");
        }
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
}
