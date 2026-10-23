using System.Security.Cryptography;
using System.Text.RegularExpressions;
using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Authentication;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

internal static class SellerRegistrationSqlChecks
{
    public static async Task RunAsync()
    {
        var connection =
            Environment.GetEnvironmentVariable("G4_REGISTRATION_SQL_TEST_CONNECTION")
            ?? throw new Exception("Registration QA connection required");
        if (
            new SqlConnectionStringBuilder(connection).InitialCatalog
            != "G4_SellerRegistration_Test_20261015"
        )
            throw new Exception("Dedicated seller registration QA database required");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection)
            .Options;
        var config = SellerRegistrationChecks.Configuration();
        var clock = new SellerRegistrationChecks.Clock();
        void Check(bool ok, string message) => SellerRegistrationChecks.Check(ok, message);
        SellerRegistrationService Service(ApplicationDbContext db) =>
            new(db, new(config), config, new SellerRegistrationChecks.Host(), clock);
        string Identity() => "998" + RandomNumberGenerator.GetInt32(1000000000).ToString("D9");
        SellerRegistrationInput Input() =>
            SellerRegistrationChecks.Input(
                $"register-{Guid.NewGuid():N}@example.test",
                Identity(),
                Guid.NewGuid().ToString()
            );
        await Bootstrap();
        var input = Input();
        async Task<SellerRegistrationResult> Register(SellerRegistrationInput r)
        {
            await using var db = new ApplicationDbContext(options);
            return await Service(db).RegisterAsync(r);
        }
        var retries = await Task.WhenAll(Register(input), Register(input));
        var sellerId = retries[0].SellerId;
        Check(
            sellerId == retries[1].SellerId,
            "SQL concurrent registration retries return same seller"
        );
        await using (var db = new ApplicationDbContext(options))
        {
            Check(
                await db.Users.CountAsync(x => x.Email == input.Email) == 1
                    && await db.Stores.CountAsync(x => x.SellerId == sellerId) == 1
                    && await db.SellerAccounts.CountAsync(x => x.SellerId == sellerId) == 1
                    && await db.SellerIdentities.CountAsync(x => x.SellerId == sellerId) == 1,
                "SQL registration is atomic with exactly one account/shop/wallet/identity"
            );
        }
        await SellerRegistrationChecks.Reject<UnauthorizedAccessException>(() =>
            Register(
                input with
                {
                    Email = $"duplicate-{Guid.NewGuid():N}@example.test",
                    Key = Guid.NewGuid().ToString(),
                }
            )
        );
        var shared = Identity();
        var a = Input() with { Identity = new("CCCD", shared) };
        var b = Input() with { Identity = new("CCCD", shared) };
        async Task<int?> TryRegister(SellerRegistrationInput r)
        {
            try
            {
                return (await Register(r)).SellerId;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
        var competing = await Task.WhenAll(TryRegister(a), TryRegister(b));
        Check(
            competing.Count(x => x.HasValue) == 1,
            "SQL two emails cannot simultaneously claim same identity"
        );
        var bankInput = Input();
        bankInput = bankInput with { Identity = new("BANK", bankInput.Identity.Value, "QABANK") };
        var bankSeller = await Register(bankInput);
        await using (var db = new ApplicationDbContext(options))
        {
            var wallet = await db.SellerAccounts.SingleAsync(x =>
                x.SellerId == bankSeller.SellerId
            );
            wallet.AvailableBalance = 5m;
            await db.SaveChangesAsync();
            var payout = await new SellerFinanceService(db, config, clock).RequestPayoutAsync(
                bankSeller.SellerId,
                1m,
                $"bank:{bankSeller.SellerId}",
                false
            );
            Check(
                payout.DestinationMasked == "QABANK · ****" + bankInput.Identity.Value[^4..],
                "SQL payout snapshots latest verified masked bank identity"
            );
        }
        await using (var db = new ApplicationDbContext(options))
        {
            var account = await db.SellerAccounts.SingleAsync(x => x.SellerId == sellerId);
            account.NegativeBalance = 40m;
            await db.SaveChangesAsync();
            await new SellerFinanceService(db, config, clock).GetDebtAsync(sellerId);
        }
        clock.Advance(300);
        async Task<int> Worker()
        {
            await using var db = new ApplicationDbContext(options);
            return await new SellerFinanceService(db, config, clock).MaintainWriteOffsAsync();
        }
        await Task.WhenAll(Worker(), Worker());
        await using (var db = new ApplicationDbContext(options))
        {
            var account = await db.SellerAccounts.SingleAsync(x => x.SellerId == sellerId);
            Check(
                account.NegativeBalance == 40m && account.PermanentSalesBlockedAt != null,
                "SQL write-off keeps debt and permanently blocks seller"
            );
            Check(
                await db.SellerDebtWriteOffs.CountAsync(x => x.SellerAccountId == account.Id) == 1
                    && await db.SellerBlacklist.CountAsync(x => x.SourceSellerId == sellerId) == 1,
                "SQL concurrent workers decide and blacklist once"
            );
            Check(
                await db.FinancialTransactions.CountAsync(x =>
                    x.SellerAccountId == account.Id && x.Type == "DebtWriteOff" && x.Amount == 0
                ) == 1,
                "write-off creates one non-monetary audit entry"
            );
        }
        await SellerRegistrationChecks.Reject<UnauthorizedAccessException>(() =>
            Register(
                input with
                {
                    Email = $"retry-ban-{Guid.NewGuid():N}@example.test",
                    Key = Guid.NewGuid().ToString(),
                }
            )
        );
        async Task<DebtRepaymentView> Repay()
        {
            await using var db = new ApplicationDbContext(options);
            return await new SellerFinanceService(db, config, clock).RepayDebtAsync(
                sellerId,
                new(40m, 40m, 1, "post-writeoff")
            );
        }
        var payments = await Task.WhenAll(Repay(), Repay());
        Check(payments[0].Id == payments[1].Id, "SQL post-writeoff repayment retries once");
        await using (var db = new ApplicationDbContext(options))
        {
            var account = await db.SellerAccounts.SingleAsync(x => x.SellerId == sellerId);
            var decision = await db.SellerDebtWriteOffs.SingleAsync(x =>
                x.SellerAccountId == account.Id
            );
            Check(
                account.NegativeBalance == 0
                    && account.PermanentSalesBlockedAt != null
                    && await db
                        .SellerWriteOffRecoveries.Where(r => r.WriteOffId == decision.Id)
                        .SumAsync(r => r.Amount) == 40m,
                "SQL recovery report reconciles full repayment and keeps permanent block"
            );
            Check(
                !await db.SellerDebtEvents.AnyAsync(x =>
                    x.SellerAccountId == account.Id && x.EventType == "DebtRestored"
                ),
                "no false restoration after permanent ban"
            );
        }
        // Finance gate and registry gate must agree on the entire identity history at decision time.
        var racer = await Register(Input());
        await using (var db = new ApplicationDbContext(options))
        {
            var account = await db.SellerAccounts.SingleAsync(x => x.SellerId == racer.SellerId);
            account.NegativeBalance = 10m;
            await db.SaveChangesAsync();
            await new SellerFinanceService(db, config, clock).GetDebtAsync(racer.SellerId);
        }
        clock.Advance(300);
        async Task Change()
        {
            await using var db = new ApplicationDbContext(options);
            try
            {
                await Service(db)
                    .ChangeIdentityAsync(
                        racer.SellerId,
                        new(new("CCCD", Identity()), 1, "identity-race")
                    );
            }
            catch (UnauthorizedAccessException) { }
        }
        await Task.WhenAll(Worker(), Change());
        await using (var db = new ApplicationDbContext(options))
        {
            var identities = await db
                .SellerIdentities.Where(x => x.SellerId == racer.SellerId)
                .ToListAsync();
            foreach (var id in identities)
                Check(
                    await db.SellerBlacklist.AnyAsync(x =>
                        x.KeyId == id.KeyId && x.Fingerprint == id.Fingerprint
                    ),
                    "identity update cannot escape simultaneous write-off blacklist"
                );
        }
        Console.WriteLine(
            "SQL registration/write-off checks passed: migration preservation, atomic registration, concurrent claims/workers/retries, identity-change race and recovery accounting"
        );

        async Task Bootstrap()
        {
            await using var db = new ApplicationDbContext(options);
            var creator = db.Database.GetService<IRelationalDatabaseCreator>();
            if (!await creator.ExistsAsync())
                await creator.CreateAsync();
            await using var sql = new SqlConnection(connection);
            await sql.OpenAsync();
            await using var exists = new SqlCommand("SELECT OBJECT_ID(N'[User]')", sql);
            if (await exists.ExecuteScalarAsync() is DBNull or null)
            {
                var baseline = Regex.Replace(
                    await File.ReadAllTextAsync("database/baseline.sql"),
                    @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE CloneEbayDB;)\s*$",
                    ""
                );
                foreach (var batch in Regex.Split(baseline, @"(?im)^\s*GO\s*$"))
                    if (!string.IsNullOrWhiteSpace(batch))
                    {
                        await using var cmd = new SqlCommand(batch, sql);
                        await cmd.ExecuteNonQueryAsync();
                    }
                await db
                    .Database.GetService<IMigrator>()
                    .MigrateAsync("20261014102533_AddSellerDebtManagement");
                await using var seed = new SqlCommand(
                    @"
                    INSERT INTO [User] ([role], [email]) VALUES (N'seller', N'legacy-registration@example.test');
                    INSERT INTO SellerAccount (SellerId, Level, Status, MonthlySalesLimit, HoldDays, ProcessingBalance,
                        AvailableBalance, OnHoldBalance, NegativeBalance, MonthlySalesAmount, SalesMonth, UpdatedAt,
                        DebtCycleNumber, DebtSuspended)
                    VALUES (SCOPE_IDENTITY(), 1, N'ManualRestricted', 5000, 21, 23, 7, 5, 50, 100, '2026-10-01', '2025-01-01', 0, 0);",
                    sql
                );
                await seed.ExecuteNonQueryAsync();
            }
            await db.Database.MigrateAsync();
            var old = await db
                .SellerAccounts.AsNoTracking()
                .SingleAsync(a =>
                    db.Users.Any(u =>
                        u.Id == a.SellerId && u.Email == "legacy-registration@example.test"
                    )
                );
            Check(
                old.NegativeBalance == 50m
                    && old.AvailableBalance == 7m
                    && old.ProcessingBalance == 23m
                    && old.OnHoldBalance == 5m
                    && old.Status == "ManualRestricted",
                "registration migration preserves legacy balances and restrictions"
            );
        }
    }
}
