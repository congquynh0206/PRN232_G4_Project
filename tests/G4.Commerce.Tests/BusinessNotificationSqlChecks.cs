using System.Text.RegularExpressions;
using G4.Application.Integrations;
using G4.Domain.Entities;
using G4.Infrastructure.Checkout;
using G4.Infrastructure.Diagnostics;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Notifications;
using G4.Infrastructure.Persistence;
using G4.Infrastructure.Returns;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

internal static class BusinessNotificationSqlChecks
{
    public static async Task RunAsync()
    {
        const string database = "G4_N1_Test_20261017";
        var connection = Environment.GetEnvironmentVariable("G4_N1_SQL_CONNECTION") ?? throw new Exception("N1 QA connection required");
        var builder = new SqlConnectionStringBuilder(connection);
        if (builder.InitialCatalog != database) throw new Exception("Only G4_N1_Test_20261017 is allowed");
        // Keep QA data/log files on the workspace drive; the Windows system drive may be full.
        var masterBuilder = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" };
        await using (var master = new SqlConnection(masterBuilder.ConnectionString))
        {
            await master.OpenAsync(); await using var exists = new SqlCommand("SELECT DB_ID(@name)", master);
            exists.Parameters.AddWithValue("@name", database);
            if (await exists.ExecuteScalarAsync() is DBNull or null)
            {
                var root = Path.GetFullPath(Environment.CurrentDirectory);
                var dir = Path.GetFullPath(Path.Combine(root, ".temp-appdata", "n1-sql"));
                if (!dir.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("QA path outside workspace");
                Directory.CreateDirectory(dir);
                var mdf = Path.Combine(dir, database + ".mdf").Replace("'", "''"); var ldf = Path.Combine(dir, database + ".ldf").Replace("'", "''");
                await using var create = new SqlCommand($"CREATE DATABASE [{database}] ON PRIMARY (NAME=N'{database}',FILENAME=N'{mdf}',SIZE=32MB,FILEGROWTH=16MB) LOG ON (NAME=N'{database}_log',FILENAME=N'{ldf}',SIZE=16MB,FILEGROWTH=16MB)", master);
                await create.ExecuteNonQueryAsync();
            }
        }
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        var config = BusinessNotificationChecks.Config(); config["Frontend:PublicBaseUrl"] = "http://127.0.0.1:5139";
        void Check(bool ok, string message) => BusinessNotificationChecks.Check(ok, message);
        await using (var db = new ApplicationDbContext(options))
        {
            await using var sql = new SqlConnection(connection); await sql.OpenAsync();
            await using var exists = new SqlCommand("SELECT OBJECT_ID(N'[User]')", sql);
            if (await exists.ExecuteScalarAsync() is DBNull or null)
            {
                var baseline = Regex.Replace(await File.ReadAllTextAsync("database/baseline.sql"), @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE CloneEbayDB;)\s*$", "");
                foreach (var batch in Regex.Split(baseline, @"(?im)^\s*GO\s*$"))
                    if (!string.IsNullOrWhiteSpace(batch)) { await using var cmd = new SqlCommand(batch, sql); await cmd.ExecuteNonQueryAsync(); }
                await db.Database.GetService<IMigrator>().MigrateAsync("20261017042643_AddProductFeedbackReplies");
                await using var seed = new SqlCommand(@"INSERT INTO NotificationOutbox (OrderId,EventType,Recipient,Subject,Body,Status,Attempts,AttemptsInCycle,Cycle,CreatedAt)
                    VALUES (NULL,N'LegacyOrphan',N'legacy@example.test',N'N1 legacy preserved',N'Preserve body',N'Sent',2,2,1,'2026-10-01'),
                           (NULL,N'LegacyOrphan',N'legacy@example.test',N'N1 legacy preserved',N'Preserve body',N'Sent',2,2,1,'2026-10-01');", sql);
                await seed.ExecuteNonQueryAsync();
            }
            await db.Database.MigrateAsync();
            Check(await db.NotificationOutbox.CountAsync(n => n.Subject == "N1 legacy preserved" && n.Status == "Sent" && n.Attempts == 2 && n.RecipientUserId == null && n.PayoutId == null) == 2,
                "migration preserves legacy messages, including null-order duplicates");
        }
        BusinessNotificationChecks.Fixture f; OrderTable unpaid, paid, returnedOrder;
        await using (var db = new ApplicationDbContext(options))
        { f = await BusinessNotificationChecks.Seed(db); unpaid = await BusinessNotificationChecks.Order(db, f); paid = await BusinessNotificationChecks.Order(db, f, "Paid"); returnedOrder = await BusinessNotificationChecks.Order(db, f, "Delivered"); }
        var suffix = Guid.NewGuid().ToString("N");
        async Task<int> Fail(string key)
        { await using var db = new ApplicationDbContext(options); return (await new CheckoutService(db, config: config).PayCardAsync(unpaid.Id, "4000000000000002", "12/30", key)).Id; }
        var attempts = await Task.WhenAll(Fail("n1-a-" + suffix), Fail("n1-a-" + suffix)); Check(attempts[0] == attempts[1], "SQL payment retry is same attempt");
        await Fail("n1-b-" + suffix);
        async Task Cancel()
        { await using var db = new ApplicationDbContext(options); await new ReturnService(db, new ReturnAutomationChecks.Gateway(), new ReturnAutomationChecks.Carrier(), config: config).RequestCancelAsync(paid.Id); }
        await Task.WhenAll(Cancel(), Cancel());
        int rejectedMail;
        await using (var db = new ApplicationDbContext(options))
        {
            Check(await db.NotificationOutbox.CountAsync(n => n.OrderId == unpaid.Id && n.EventType == "PaymentFailed") == 2, "SQL permits separate failed attempts while deduping same attempt");
            Check(await db.NotificationOutbox.CountAsync(n => n.OrderId == paid.Id && n.EventType == "CancellationRequested") == 2, "SQL one request emits buyer/seller messages without unique-index collision");
            var returns = new ReturnService(db, new ReturnAutomationChecks.Gateway(), new ReturnAutomationChecks.Carrier(), config: config);
            var quote = await returns.QuoteAsync(returnedOrder.Id, "WrongItem");
            var request = await returns.RequestPricedReturnAsync(returnedOrder.Id, new("WrongItem", "Wrong product", "n1-return-" + suffix, quote.PricingFingerprint));
            await returns.RejectAsync(request.Id, "Cần thêm bằng chứng <img src=x onerror=bad()>");
            rejectedMail = (await db.NotificationOutbox.SingleAsync(n => n.OrderId == returnedOrder.Id && n.EventType == "ReturnRejected")).Id;
        }
        async Task<int> Payout()
        { await using var db = new ApplicationDbContext(options); return (await new SellerFinanceService(db, config).RequestPayoutAsync(f.Seller, 10, "n1-payout-" + suffix, false)).Id; }
        var payouts = await Task.WhenAll(Payout(), Payout()); Check(payouts[0] == payouts[1], "SQL payout retry shares original payout");
        await using (var db = new ApplicationDbContext(options))
        {
            var finance = new SellerFinanceService(db, config); var returned = await finance.RequestPayoutAsync(f.Seller, 12, "n1-payout-return-" + suffix, true);
            for (var i = 0; i < 4; i++) await finance.AdvancePayoutsAsync();
            Check(await db.NotificationOutbox.CountAsync(n => n.PayoutId == payouts[0]) == 1 && await db.NotificationOutbox.CountAsync(n => n.PayoutId == returned.Id) == 1, "SQL terminal payout emails saved once");
            var q = new DiagnosticsQueryService(db);
            Check((await q.NotificationsAsync(f.Seller, "seller", new(EventType: "PaymentFailed"), default)).TotalCount == 0, "SQL seller cannot see buyer-only failures");
            Check((await q.NotificationsAsync(f.Buyer, "buyer", new(EventType: "PayoutReturned"), default)).TotalCount == 0, "SQL buyer cannot see seller payout");
            await ProductFeedbackChecks.Reject<KeyNotFoundException>(() => q.NotificationAsync(rejectedMail, f.Seller, "seller", default));
        }
        // Check the event-key constraint independently of service-level locks/checks.
        await using (var db = new ApplicationDbContext(options))
        {
            var original = await db.NotificationOutbox.FirstAsync(n => n.OrderId == unpaid.Id && n.EventType == "PaymentFailed");
            db.NotificationOutbox.Add(new NotificationOutbox { OrderId = unpaid.Id, EventType = "PaymentFailed", EventKey = original.EventKey, RecipientUserId = f.Buyer, Recipient = original.Recipient, CreatedAt = DateTime.UtcNow });
            await ProductFeedbackChecks.Reject<DbUpdateException>(() => db.SaveChangesAsync());
        }
        OrderTable rollbackOrder;
        await using (var db = new ApplicationDbContext(options)) rollbackOrder = await BusinessNotificationChecks.Order(db, f);
        await using (var db = new ApplicationDbContext(options))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await new CheckoutService(db, config: config).PayCardAsync(rollbackOrder.Id, "4000000000000002", "12/30", "n1-rollback-" + suffix);
            await tx.RollbackAsync();
        }
        await using (var db = new ApplicationDbContext(options))
            Check(!await db.Payments.AnyAsync(p => p.OrderId == rollbackOrder.Id) && !await db.NotificationOutbox.AnyAsync(n => n.OrderId == rollbackOrder.Id), "rollback removes both payment and email");
        var factory = new NotificationProcessorBehaviorChecks.Factory(options); config["Mail:Mode"] = "Capture";
        await new NotificationProcessor(factory, new NoSender(), config, TimeProvider.System).ProcessDueAsync(100, default);
        await using (var db = new ApplicationDbContext(options))
        {
            Check(await db.NotificationOutbox.AnyAsync(n => n.OrderId == paid.Id && n.EventType == "CancellationRequested" && n.Status == "Captured" && n.SentAt == null), "capture notification does not claim email sent");
            var seller = await db.Users.SingleAsync(u => u.Id == f.Seller); var buyer = await db.Users.SingleAsync(u => u.Id == f.Buyer); var other = await db.Users.SingleAsync(u => u.Id == f.OtherBuyer);
            var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2("NoticeDemo123!", salt, 210000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
            var password = $"PBKDF2$210000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
            seller.Password = buyer.Password = other.Password = password;
            var admin = new User { Role = "admin", Username = "N1 Admin", Email = "n1-admin-" + suffix + "@example.test", Password = password };
            db.Users.Add(admin); await db.SaveChangesAsync();
            Directory.CreateDirectory("artifacts/n1-notifications-qa");
            await File.WriteAllTextAsync("artifacts/n1-notifications-qa/fixture.json", System.Text.Json.JsonSerializer.Serialize(new { buyerEmail = buyer.Email, sellerEmail = seller.Email,
                otherBuyerEmail = other.Email, adminEmail = admin.Email, orderId = paid.Id, rejectedOrderId = returnedOrder.Id, rejectedMailId = rejectedMail, payoutId = payouts[0] }));
        }
        Console.WriteLine("N1 SQL checks passed: migration preservation, concurrent attempts/recipients/payouts, event uniqueness, privacy, outbox rollback and capture; browser QA fixture ready.");
    }
    private sealed class NoSender : IEmailSender
    { public Task SendAsync(EmailEnvelope email, CancellationToken ct) => throw new Exception("Capture must not send SMTP"); }
}
