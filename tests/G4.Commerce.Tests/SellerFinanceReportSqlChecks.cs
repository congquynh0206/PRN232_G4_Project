using System.Security.Cryptography;
using System.Text.RegularExpressions;
using G4.Domain.Entities;
using G4.Infrastructure.Finance;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

internal static class SellerFinanceReportSqlChecks
{
    public static async Task RunAsync()
    {
        var connection =
            Environment.GetEnvironmentVariable("G4_SELLER_REPORT_SQL_TEST_CONNECTION")
            ?? throw new Exception("Dedicated seller report SQL QA connection required");
        if (
            new SqlConnectionStringBuilder(connection).InitialCatalog
            != "G4_SellerFinanceReport_Test_20261020"
        )
            throw new Exception(
                "Refusing any database other than G4_SellerFinanceReport_Test_20261020"
            );
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        var creator = db.Database.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync())
            await creator.CreateAsync();
        await using (var sql = new SqlConnection(connection))
        {
            await sql.OpenAsync();
            await using var exists = new SqlCommand("SELECT OBJECT_ID(N'[User]')", sql);
            if (await exists.ExecuteScalarAsync() is DBNull or null)
            {
                // The legacy InitialCreate migration is empty. Bootstrap only this guarded QA database.
                var baseline = Regex.Replace(
                    await File.ReadAllTextAsync("database/baseline.sql"),
                    @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE CloneEbayDB;)\s*$",
                    ""
                );
                foreach (var batch in Regex.Split(baseline, @"(?im)^\s*GO\s*$"))
                    if (!string.IsNullOrWhiteSpace(batch))
                    {
                        await using var command = new SqlCommand(batch, sql);
                        await command.ExecuteNonQueryAsync();
                    }
            }
        }
        await db.Database.MigrateAsync();
        var token = Guid.NewGuid().ToString("N");
        var password = "G4ReportQaOnly2026!";
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Convert.ToBase64String(
            Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32)
        );
        var encoded = $"PBKDF2$100000${Convert.ToBase64String(salt)}${hash}";
        var seller = new User
        {
            Role = "seller",
            Email = $"report-{token}@example.test",
            Password = encoded,
        };
        var other = new User { Role = "seller", Email = $"other-{token}@example.test" };
        var buyer = new User
        {
            Role = "buyer",
            Email = $"buyer-report-{token}@example.test",
            Password = encoded,
        };
        db.Users.AddRange(seller, other, buyer);
        await db.SaveChangesAsync();
        var account = new SellerAccount
        {
            SellerId = seller.Id,
            SalesMonth = new DateTime(2026, 10, 1),
        };
        var otherAccount = new SellerAccount
        {
            SellerId = other.Id,
            SalesMonth = new DateTime(2026, 10, 1),
        };
        db.SellerAccounts.AddRange(account, otherAccount);
        var saleDay = new DateTime(2026, 10, 18, 18, 0, 0, DateTimeKind.Utc);
        var refundDay = saleDay.AddDays(1);
        var sale = new OrderTable
        {
            SellerId = seller.Id,
            Status = "Paid",
            TotalPrice = 100m,
        };
        var otherOrder = new OrderTable
        {
            SellerId = other.Id,
            Status = "Paid",
            TotalPrice = 900m,
        };
        var missing = new OrderTable
        {
            SellerId = seller.Id,
            Status = "Paid",
            TotalPrice = 5m,
        };
        db.OrderTables.AddRange(sale, otherOrder, missing);
        await db.SaveChangesAsync();
        var payment = new Payment
        {
            OrderId = sale.Id,
            Status = "Succeeded",
            Amount = 100m,
            PaidAt = saleDay,
        };
        var otherPayment = new Payment
        {
            OrderId = otherOrder.Id,
            Status = "Succeeded",
            Amount = 900m,
            PaidAt = saleDay,
        };
        var missingPayment = new Payment
        {
            OrderId = missing.Id,
            Status = "Succeeded",
            Amount = 5m,
            PaidAt = saleDay,
        };
        db.Payments.AddRange(payment, otherPayment, missingPayment);
        await db.SaveChangesAsync();
        var settlement = new SellerSettlement
        {
            SellerId = seller.Id,
            SellerAccountId = account.Id,
            OrderId = sale.Id,
            PaymentId = payment.Id,
        };
        var otherSettlement = new SellerSettlement
        {
            SellerId = other.Id,
            SellerAccountId = otherAccount.Id,
            OrderId = otherOrder.Id,
            PaymentId = otherPayment.Id,
        };
        db.SellerSettlements.AddRange(settlement, otherSettlement);
        await db.SaveChangesAsync();
        var refund = new Refund
        {
            OrderId = sale.Id,
            PaymentId = payment.Id,
            Amount = 20m,
            Status = "Succeeded",
            CompletedAt = refundDay,
            IdempotencyKey = "report-" + token,
        };
        db.Refunds.Add(refund);
        await db.SaveChangesAsync();
        void Add(
            string type,
            decimal amount,
            int accountId,
            int orderId,
            int settlementId,
            int? refundId = null
        ) =>
            db.FinancialTransactions.Add(
                new FinancialTransaction
                {
                    SellerAccountId = accountId,
                    SettlementId = settlementId,
                    OrderId = orderId,
                    RefundId = refundId,
                    Type = type,
                    Bucket = "Processing",
                    Amount = amount,
                    EntryKey = $"report:{token}:{Guid.NewGuid():N}",
                    CreatedAt = refundDay.AddDays(1),
                }
            );
        Add("Sale", 100m, account.Id, sale.Id, settlement.Id);
        Add("PlatformFee", -12.30m, account.Id, sale.Id, settlement.Id);
        Add("Refund", -20m, account.Id, sale.Id, settlement.Id, refund.Id);
        Add("FeeCredit", 2.46m, account.Id, sale.Id, settlement.Id, refund.Id);
        Add("RefundApplied", 0m, account.Id, sale.Id, settlement.Id, refund.Id);
        Add("Sale", 900m, otherAccount.Id, otherOrder.Id, otherSettlement.Id);
        Add("Payout", -50m, account.Id, sale.Id, settlement.Id);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new SellerFinanceReportService(db, TimeProvider.System);
        var start = new DateOnly(2026, 10, 19);
        var end = new DateOnly(2026, 10, 20);
        var report = await service.GetAsync(seller.Id, start, end, "day", "Asia/Ho_Chi_Minh", 1, 1);
        Check(
            report.Totals.BuyerPaid == 100m
                && report.Totals.BuyerRefunded == 20m
                && report.Totals.PlatformFee == 9.84m
                && report.Totals.Net == 70.16m
                && report.Totals.PaidOrders == 1,
            "real SQL report arithmetic and seller isolation"
        );
        Check(
            report.Chart.Count == 2
                && report.Chart[0].Net == 87.70m
                && report.Chart[1].Net == -17.54m,
            "real SQL delayed ledger postings attributed to business dates"
        );
        Check(
            report.TotalCount == 2
                && report.Items.Count == 1
                && report.Items[0].Sources?.Count == 2,
            "real SQL paging does not truncate chart or totals"
        );
        Check(
            report.UnreconciledPayments == 1 && report.UnreconciledRefunds == 0,
            "real SQL reconciliation predicates"
        );
        var otherReport = await service.GetAsync(other.Id, start, end, "day", "Asia/Ho_Chi_Minh");
        Check(
            otherReport.Totals.BuyerPaid == 900m && otherReport.Totals.BuyerRefunded == 0m,
            "other seller cannot see this seller's transactions"
        );
        var mismatchedSettlement = new SellerSettlement
        {
            SellerId = seller.Id,
            SellerAccountId = account.Id,
            OrderId = missing.Id,
            PaymentId = missingPayment.Id,
        };
        var markerOnly = new Refund
        {
            OrderId = sale.Id,
            PaymentId = payment.Id,
            Amount = 5m,
            Status = "Succeeded",
            CompletedAt = refundDay,
            IdempotencyKey = "marker-" + token,
        };
        db.SellerSettlements.Add(mismatchedSettlement);
        db.Refunds.Add(markerOnly);
        await db.SaveChangesAsync();
        Add("Sale", 5m, otherAccount.Id, missing.Id, mismatchedSettlement.Id);
        Add("RefundApplied", 0m, account.Id, sale.Id, settlement.Id, markerOnly.Id);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var incomplete = await service.GetAsync(seller.Id, start, end, "day", "Asia/Ho_Chi_Minh");
        Check(
            incomplete.UnreconciledPayments == 1 && incomplete.UnreconciledRefunds == 1,
            "wrong-account sale and marker-only refund must be flagged as unreconciled"
        );
        Check(
            incomplete.Totals.BuyerPaid == 100m && incomplete.Totals.BuyerRefunded == 20m,
            "unreconciled events must not invent or leak money"
        );
        var wrongOwnerOrder = new OrderTable
        {
            SellerId = seller.Id,
            Status = "Paid",
            TotalPrice = 77m,
        };
        db.OrderTables.Add(wrongOwnerOrder);
        await db.SaveChangesAsync();
        var wrongOwnerPayment = new Payment
        {
            OrderId = wrongOwnerOrder.Id,
            Status = "Succeeded",
            Amount = 77m,
            PaidAt = saleDay,
        };
        db.Payments.Add(wrongOwnerPayment);
        await db.SaveChangesAsync();
        var wrongOwnerSettlement = new SellerSettlement
        {
            SellerId = seller.Id,
            SellerAccountId = otherAccount.Id,
            OrderId = wrongOwnerOrder.Id,
            PaymentId = wrongOwnerPayment.Id,
        };
        db.SellerSettlements.Add(wrongOwnerSettlement);
        await db.SaveChangesAsync();
        Add("Sale", 77m, otherAccount.Id, wrongOwnerOrder.Id, wrongOwnerSettlement.Id);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var isolated = await service.GetAsync(seller.Id, start, end, "day", "Asia/Ho_Chi_Minh");
        Check(
            isolated.Totals.BuyerPaid == 100m && isolated.UnreconciledPayments == 2,
            "settlement with a different seller's account cannot leak sale or count as reconciled"
        );
        Console.WriteLine(
            $"Seller report SQL: migrated isolated QA DB, money, backfill, refunds, warnings, paging and seller isolation passed; HTTP QA identities: {seller.Email}, {buyer.Email}"
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
