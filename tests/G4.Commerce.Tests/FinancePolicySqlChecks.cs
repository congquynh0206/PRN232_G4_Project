using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Finance;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

internal static class FinancePolicySqlChecks
{
    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("G4_FINANCE_SQL_TEST_CONNECTION") ?? throw new Exception("QA connection required");
        if (new SqlConnectionStringBuilder(connection).InitialCatalog != "G4_Finance_Test_20261014")
            throw new Exception("Dedicated finance SQL QA database required");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        var clock = new Clock();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Finance:Levels:1:HoldSeconds"] = "30", ["Finance:Levels:2:HoldSeconds"] = "15" }).Build();
        int adminId, buyerId, sellerId, accountId, orderId, secondOrderId, paymentId, secondPaymentId;
        var token = Guid.NewGuid().ToString("N");
        await using (var db = new ApplicationDbContext(options))
        {
            var creator = db.Database.GetService<IRelationalDatabaseCreator>();
            if (!await creator.ExistsAsync()) await creator.CreateAsync();
            await using (var sql = new SqlConnection(connection))
            {
                await sql.OpenAsync();
                await using var exists = new SqlCommand("SELECT OBJECT_ID(N'[User]')", sql);
                if (await exists.ExecuteScalarAsync() is DBNull or null)
                {
                    // InitialCreate is intentionally empty: bootstrap only this guarded QA database.
                    var baseline = await File.ReadAllTextAsync("database/baseline.sql");
                    baseline = System.Text.RegularExpressions.Regex.Replace(baseline,
                        @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE CloneEbayDB;)\s*$", "");
                    foreach (var batch in System.Text.RegularExpressions.Regex.Split(baseline, @"(?im)^\s*GO\s*$"))
                        if (!string.IsNullOrWhiteSpace(batch))
                        {
                            await using var command = new SqlCommand(batch, sql);
                            await command.ExecuteNonQueryAsync();
                        }
                }
            }
            await db.Database.MigrateAsync();
            var admin = new User { Username = "Finance QA admin", Role = "admin", Email = $"admin-{token}@example.test" };
            var buyer = new User { Role = "buyer", Email = $"buyer-{token}@example.test" };
            var seller = new User { Role = "seller", Email = $"seller-{token}@example.test" };
            db.Users.AddRange(admin, buyer, seller);
            await db.SaveChangesAsync();
            (adminId, buyerId, sellerId) = (admin.Id, buyer.Id, seller.Id);
            var feeService = new PlatformFeeService(db, clock);
            var latest = (await feeService.ReadAsync(adminId)).LatestPolicyId;
            await feeService.ChangeAsync(adminId, new(12m, .30m, null, "QA starting fee", "start-" + token, latest));
            clock.Advance(1);
            var account = new SellerAccount { SellerId = sellerId, Level = 3, MonthlySalesLimit = 1000000m, SalesMonth = new DateTime(clock.Utc.Year, clock.Utc.Month, 1) };
            db.SellerAccounts.Add(account);
            await db.SaveChangesAsync();
            accountId = account.Id;
            async Task<(int, int)> AddOrder()
            {
                var order = new OrderTable { SellerId = sellerId, BuyerId = buyerId, Status = "Paid", TotalPrice = 100m, OrderDate = clock.Utc };
                db.OrderTables.Add(order); await db.SaveChangesAsync();
                var payment = new Payment { OrderId = order.Id, Status = "Succeeded", Method = "Card", Amount = 100m, PaidAt = clock.Utc };
                db.Payments.Add(payment); await db.SaveChangesAsync();
                return (order.Id, payment.Id);
            }
            (orderId, paymentId) = await AddOrder();
            (secondOrderId, secondPaymentId) = await AddOrder();
        }
        async Task<int> Settle(int order, int payment)
        {
            await using var db = new ApplicationDbContext(options);
            return (await new SellerFinanceService(db, config, clock).RecordSuccessfulPaymentAsync(order, payment)).Id;
        }
        var duplicate = await Task.WhenAll(Settle(orderId, paymentId), Settle(orderId, paymentId));
        Check(duplicate[0] == duplicate[1], "SQL simultaneous payment retries settle once");
        await Task.WhenAll(Settle(orderId, paymentId), Settle(secondOrderId, secondPaymentId));
        await using (var db = new ApplicationDbContext(options))
        {
            var account = await db.SellerAccounts.SingleAsync(x => x.Id == accountId);
            Check(account.AvailableBalance == 175.40m && account.ProcessingBalance == 0, "SQL two orders credit one seller without lost updates");
            Check(await db.FinancialTransactions.CountAsync(x => x.SellerAccountId == accountId && x.Type == "FundsReleased") == 2, "SQL one wallet release per order");
            Check(!await db.SellerPayouts.AnyAsync(x => x.SellerAccountId == accountId), "SQL Instant has no payout record");
        }
        int latestId;
        await using (var db = new ApplicationDbContext(options))
            latestId = (await new PlatformFeeService(db, clock).ReadAsync(adminId)).LatestPolicyId;
        var request = new PlatformFeeChangeRequest(13m, .30m, clock.GetUtcNow().AddDays(1), "SQL concurrent audit", $"fee-{token}", latestId);
        async Task<int> Change(PlatformFeeChangeRequest r)
        {
            await using var db = new ApplicationDbContext(options);
            return await new PlatformFeeService(db, clock).ChangeAsync(adminId, r);
        }
        var changes = await Task.WhenAll(Change(request), Change(request));
        Check(changes[0] == changes[1], "SQL duplicate admin submission creates one audit");
        clock.Advance(1);
        async Task<bool> CompetingChange(string key)
        {
            try { await Change(request with { RequestKey = key, EffectiveAt = null, ExpectedLatestPolicyId = changes[0] }); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var competing = await Task.WhenAll(CompetingChange($"a-{token}"), CompetingChange($"b-{token}"));
        Check(competing.Count(x => x) == 1, "SQL only one admin version wins concurrent updates");
        await using (var db = new ApplicationDbContext(options))
        {
            var rows = await db.SellerSettlements.Where(x => x.SellerAccountId == accountId).ToListAsync();
            Check(rows.All(x => x.PlatformFeeAmount == 12.30m), "SQL paid orders keep original fees");
            var account = await db.SellerAccounts.SingleAsync(x => x.Id == accountId);
            account.Level = 2; account.HoldDays = 7;
            var order = new OrderTable { SellerId = sellerId, BuyerId = buyerId, Status = "Paid", TotalPrice = 100m, OrderDate = clock.Utc };
            db.OrderTables.Add(order); await db.SaveChangesAsync();
            var payment = new Payment { OrderId = order.Id, Status = "Succeeded", Amount = 100m, PaidAt = clock.Utc };
            db.Payments.Add(payment); await db.SaveChangesAsync();
            var finance = new SellerFinanceService(db, config, clock);
            var settlement = await finance.RecordSuccessfulPaymentAsync(order.Id, payment.Id);
            Check(settlement.ReleaseAt == null, "SQL Level 2 waits for delivery");
            clock.Advance(3600);
            order.Status = "Delivered";
            db.ShippingInfos.Add(new ShippingInfo { OrderId = order.Id, Direction = "Outbound", Status = "Delivered", DeliveredAt = clock.Utc });
            await db.SaveChangesAsync();
            // Recover a missed tracking hook from persisted DeliveredAt.
            Check(await finance.ReleaseDueFundsAsync() == 0 && settlement.ReleaseAt == clock.Utc.AddSeconds(15), "SQL recovery schedules from actual delivery");
            clock.Advance(14);
            Check(await finance.ReleaseDueFundsAsync() == 0, "SQL hold not released early");
            clock.Advance(1);
        }
        async Task<int> Release()
        {
            await using var db = new ApplicationDbContext(options);
            return await new SellerFinanceService(db, config, clock).ReleaseDueFundsAsync();
        }
        var released = await Task.WhenAll(Release(), Release());
        Check(released.Sum() == 1, "SQL concurrent workers credit due funds once");
        Console.WriteLine("SQL finance migration, concurrent Instant settlement, fee configuration/version audit and delivery hold recovery passed");
    }
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public DateTime Utc => now.UtcDateTime;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }
}
