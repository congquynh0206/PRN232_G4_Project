using System.Text.RegularExpressions;
using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

internal static class SellerLevelSqlChecks
{
    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("G4_LEVEL_SQL_TEST_CONNECTION") ?? throw new Exception("Level SQL QA connection required");
        if (new SqlConnectionStringBuilder(connection).InitialCatalog != "G4_SellerLevels_Test_20261015")
            throw new Exception("Only the dedicated seller levels QA database is allowed");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        var config = SellerLevelChecks.Config(); var clock = new SellerRegistrationChecks.Clock();
        void Check(bool ok, string message) => SellerLevelChecks.Check(ok, message);
        await using (var db = new ApplicationDbContext(options))
        {
            var creator = db.Database.GetService<IRelationalDatabaseCreator>();
            if (!await creator.ExistsAsync()) await creator.CreateAsync();
            await using var sql = new SqlConnection(connection); await sql.OpenAsync();
            await using var exists = new SqlCommand("SELECT OBJECT_ID(N'[User]')", sql);
            if (await exists.ExecuteScalarAsync() is DBNull or null)
            {
                var baseline = Regex.Replace(await File.ReadAllTextAsync("database/baseline.sql"), @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE CloneEbayDB;)\s*$", "");
                foreach (var batch in Regex.Split(baseline, @"(?im)^\s*GO\s*$"))
                    if (!string.IsNullOrWhiteSpace(batch)) { await using var cmd = new SqlCommand(batch, sql); await cmd.ExecuteNonQueryAsync(); }
                await db.Database.GetService<IMigrator>().MigrateAsync("20261015041522_AddSellerRegistrationAndAutomaticWriteOff");
                await using var sentinel = new SqlCommand(@"INSERT INTO [User] ([role],[email]) VALUES (N'seller',N'legacy-levels@example.test');
                    INSERT INTO SellerAccount (SellerId,Level,Status,MonthlySalesLimit,HoldDays,ProcessingBalance,AvailableBalance,OnHoldBalance,
                    NegativeBalance,MonthlySalesAmount,SalesMonth,UpdatedAt,DebtCycleNumber,DebtSuspended,IdentityVersion)
                    VALUES (SCOPE_IDENTITY(),2,N'ManualRestricted',10000,7,23,7,5,0,100,'2026-10-01','2025-01-01',0,0,0);", sql);
                await sentinel.ExecuteNonQueryAsync();
            }
            await db.Database.MigrateAsync();
            var old = await db.SellerAccounts.AsNoTracking().SingleAsync(x => db.Users.Any(u => u.Id == x.SellerId && u.Email == "legacy-levels@example.test"));
            Check(old.AvailableBalance == 7 && old.ProcessingBalance == 23 && old.OnHoldBalance == 5 && old.Status == "ManualRestricted", "migration preserves old money/restrictions");
        }
        (int Seller, int Buyer, int[] Orders) s;
        await using (var db = new ApplicationDbContext(options)) s = await SellerLevelChecks.Seed(db, clock.Utc, 100, false);
        async Task<SellerFeedbackView> Feedback(int rating = 5)
        {
            await using var db = new ApplicationDbContext(options);
            return await new SellerFeedbackService(db, clock).SubmitAsync(s.Orders[0], s.Buyer, new(rating, "SQL QA", "sql-feedback"));
        }
        var retries = await Task.WhenAll(Feedback(), Feedback());
        Check(retries[0].Id == retries[1].Id, "SQL concurrent feedback retries commit once");
        await SellerRegistrationChecks.Reject<InvalidOperationException>(() => Feedback(1));
        async Task<int> Worker()
        {
            await using var db = new ApplicationDbContext(options); return await new SellerFinanceService(db, config, clock).MaintainLevelsAsync();
        }
        await Task.WhenAll(Worker(), Worker());
        await using (var db = new ApplicationDbContext(options))
        {
            var a = await db.SellerAccounts.SingleAsync(x => x.SellerId == s.Seller);
            Check(a.Level == 3 && await db.SellerLevelEvaluations.CountAsync(x => x.SellerId == s.Seller) == 1,
                "SQL overlapping workers apply one due decision with real feedback and deadlines");
            Check(a.MonthlySalesAmount == 123 && a.AvailableBalance == 5, "level evaluation preserves sales and wallet");
        }
        clock.Advance(15);
        async Task Payment()
        {
            await using var db = new ApplicationDbContext(options);
            var p = await db.Payments.SingleAsync(x => x.OrderId == s.Orders[1]);
            await new SellerFinanceService(db, config, clock).RecordSuccessfulPaymentAsync(s.Orders[1], p.Id);
        }
        await Task.WhenAll(Worker(), Payment());
        await using (var db = new ApplicationDbContext(options))
        {
            var details = await new SellerFinanceService(db, config, clock).GetLevelDetailsAsync(s.Seller);
            Check(details.Level == 3 && details.History.Count == 2 && details.Preview.Metrics.DeliveryCount == 100,
                "SQL worker/payment race completes and history/metrics translate correctly");
            Check(await db.SellerSettlements.CountAsync(x => x.OrderId == s.Orders[1] && x.SellerLevelSnapshot == 3) == 1,
                "payment uses serialized account level policy");
        }
        string sellerEmail, buyerEmail, adminEmail;
        await using (var db = new ApplicationDbContext(options))
        {
            var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2("LevelDemo123!", salt, 210000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
            var password = $"PBKDF2$210000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
            var seller = await db.Users.SingleAsync(x => x.Id == s.Seller); seller.Password = password; sellerEmail = seller.Email!;
            var buyer = await db.Users.SingleAsync(x => x.Id == s.Buyer); buyer.Password = password; buyerEmail = buyer.Email!;
            adminEmail = $"level-admin-{Guid.NewGuid():N}@example.test";
            db.Users.Add(new User { Role = "admin", Email = adminEmail, Username = "Level QA admin", Password = password });
            await db.SaveChangesAsync();
        }
        Console.WriteLine($"SQL seller levels passed: migration preservation, feedback retry/conflict, overlapping workers, payment race; QA seller {s.Seller}, buyer {s.Buyer}, order {s.Orders[2]}");
        Directory.CreateDirectory("artifacts/seller-levels-qa");
        await File.WriteAllTextAsync("artifacts/seller-levels-qa/fixture.json", System.Text.Json.JsonSerializer.Serialize(new { sellerId = s.Seller, buyerId = s.Buyer, orderId = s.Orders[2], sellerEmail, buyerEmail, adminEmail }));
    }
}
