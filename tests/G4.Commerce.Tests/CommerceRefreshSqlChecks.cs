using System.Text.RegularExpressions;
using G4.Contracts.Checkout;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Payments;
using G4.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

internal static class CommerceRefreshSqlChecks
{
    public static async Task RunAsync()
    {
        var connection =
            Environment.GetEnvironmentVariable("G4_REFRESH_SQL_TEST_CONNECTION")
            ?? throw new Exception("Commerce refresh QA connection required");
        if (
            new SqlConnectionStringBuilder(connection).InitialCatalog
            != "G4_CommerceRefresh_Test_20261015"
        )
            throw new Exception("Dedicated commerce refresh QA database required");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection)
            .Options;
        var config = new ConfigurationBuilder().Build();
        var gateway = new PayPalCartChecks.BatchFake
        {
            Prefix = "PP-SQL-" + Guid.NewGuid().ToString("N")[..12],
        };
        void Check(bool yes, string message) => PayPalCartChecks.Check(yes, message);
        await using (var db = new ApplicationDbContext(options))
        {
            var creator = db.Database.GetService<IRelationalDatabaseCreator>();
            if (!await creator.ExistsAsync())
                await creator.CreateAsync();
            await using var sql = new SqlConnection(connection);
            await sql.OpenAsync();
            await using var exists = new SqlCommand("SELECT OBJECT_ID(N'[User]')", sql);
            if (await exists.ExecuteScalarAsync() is null or DBNull)
            {
                await Script(sql, "database/baseline.sql");
                await db
                    .Database.GetService<IMigrator>()
                    .MigrateAsync("20261015102627_AddSellerLevelsAndOrderFeedback");
                await using var sentinel = new SqlCommand(
                    @"INSERT INTO [User] ([role],[email]) VALUES (N'seller',N'legacy-refresh@example.test');
                    INSERT INTO SellerAccount (SellerId,Level,Status,MonthlySalesLimit,HoldDays,ProcessingBalance,AvailableBalance,OnHoldBalance,
                    NegativeBalance,MonthlySalesAmount,SalesMonth,UpdatedAt,DebtCycleNumber,DebtSuspended,IdentityVersion)
                    VALUES (SCOPE_IDENTITY(),2,N'ManualRestricted',10000,7,23,7,5,0,100,'2026-10-01','2025-01-01',0,0,0);",
                    sql
                );
                await sentinel.ExecuteNonQueryAsync();
            }
            await db.Database.MigrateAsync();
            var legacy = await db.SellerAccounts.SingleAsync(a =>
                db.Users.Any(u => u.Id == a.SellerId && u.Email == "legacy-refresh@example.test")
            );
            Check(
                legacy.AvailableBalance == 7
                    && legacy.ProcessingBalance == 23
                    && legacy.OnHoldBalance == 5
                    && legacy.Status == "ManualRestricted",
                "new schema preserves legacy money/restrictions"
            );
            await Script(sql, "database/seed-data.sql");
            if (!await db.Users.AnyAsync(u => u.Email == "isolated-refresh@example.test"))
            {
                var password = await db
                    .Users.Where(u => u.Email == "buyer@example.test")
                    .Select(u => u.Password)
                    .SingleAsync();
                db.Users.Add(
                    new G4.Domain.Entities.User
                    {
                        Role = "buyer",
                        Email = "isolated-refresh@example.test",
                        Username = "Isolated QA buyer",
                        Password = password,
                    }
                );
                await db.SaveChangesAsync();
            }
        }
        (int Buyer, int[] Orders, string Key) fixture;
        await using (var db = new ApplicationDbContext(options))
            fixture = await PayPalCartChecks.Seed(db);
        async Task<PayPalCartView> Start()
        {
            await using var db = new ApplicationDbContext(options);
            return await new PayPalCartPaymentService(
                db,
                gateway,
                config,
                new SellerFinanceService(db, config)
            ).StartAsync(fixture.Buyer, new(fixture.Key, "sql-start"));
        }
        var starts = await Task.WhenAll(Start(), Start());
        Check(
            starts[0].Id == starts[1].Id && gateway.Creates == 1,
            "concurrent start uses one provider request ID and approval"
        );
        async Task<PayPalCartView> Capture()
        {
            await using var db = new ApplicationDbContext(options);
            return await new PayPalCartPaymentService(
                db,
                gateway,
                config,
                new SellerFinanceService(db, config)
            ).CaptureAsync(fixture.Buyer, starts[0].Id, gateway.Prefix + "-1");
        }
        var captures = await Task.WhenAll(Capture(), Capture());
        Check(
            captures.All(c => c.Status == "Succeeded"),
            "overlapping capture requests reconcile to success"
        );
        await using (var db = new ApplicationDbContext(options))
        {
            Check(
                await db.SellerSettlements.CountAsync(s => fixture.Orders.Contains(s.OrderId)) == 2,
                "exactly one settlement per seller order"
            );
            Check(
                await db.FinancialTransactions.CountAsync(t =>
                    fixture.Orders.Contains(t.OrderId ?? 0) && t.Type == "Sale"
                ) == 2,
                "no duplicated money from cart capture retries"
            );
            var payments = await db
                .Payments.Where(p => p.PayPalCheckoutId == starts[0].Id)
                .ToListAsync();
            Check(
                payments.Count == 2
                    && payments.Select(p => p.ProviderTransactionId).Distinct().Count() == 2,
                "one provider order stores distinct per-order capture references"
            );
            var before = await db.CommerceChanges.CountAsync();
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                var o = await db.OrderTables.SingleAsync(o => o.Id == fixture.Orders[0]);
                o.Status = "Preparing";
                await db.SaveChangesAsync();
                Check(
                    await db.CommerceChanges.CountAsync() > before,
                    "business save writes its realtime event in same transaction"
                );
                await tx.RollbackAsync();
            }
            db.ChangeTracker.Clear();
            Check(
                await db.CommerceChanges.CountAsync() == before,
                "rolled-back business save cannot produce a committed realtime event"
            );
            Check(
                (await db.OrderTables.SingleAsync(o => o.Id == fixture.Orders[0])).Status == "Paid",
                "rollback keeps prior order state"
            );
            await G4.Infrastructure.Orders.SellerReturnList.ReadAsync(
                db,
                1,
                "approval",
                1,
                10,
                default
            );
        }
        Console.WriteLine(
            "SQL commerce refresh passed: additive migration, parallel cart create/capture, distinct refund captures, no double settlement, atomic realtime rollback and return list SQL translation"
        );

        static async Task Script(SqlConnection sql, string path)
        {
            var text = Regex.Replace(
                await File.ReadAllTextAsync(path),
                @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE\s+\[?CloneEbayDB\]?;)\s*$",
                ""
            );
            foreach (var batch in Regex.Split(text, @"(?im)^\s*GO\s*$"))
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    await using var command = new SqlCommand(batch, sql);
                    command.CommandTimeout = 60;
                    await command.ExecuteNonQueryAsync();
                }
        }
    }
}
