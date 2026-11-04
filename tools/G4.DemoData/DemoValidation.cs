using System.Data;
using G4.Infrastructure.Authentication;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace G4.DemoData;

internal static class DemoValidation
{
    public static async Task RunAsync(
        ApplicationDbContext db,
        DemoFixture fixture,
        string source,
        string imported
    )
    {
        await using var from = new SqlConnection(source);
        await using var to = new SqlConnection(imported);
        await from.OpenAsync();
        await to.OpenAsync();
        foreach (var table in await DemoExporter.TablesAsync(from))
        {
            var name = DemoExporter.Identifier(table);
            await using var a = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo." + name, from);
            await using var b = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo." + name, to);
            Check(
                Equals(await a.ExecuteScalarAsync(), await b.ExecuteScalarAsync()),
                "row count " + table
            );
        }
        await using (
            var check = new SqlCommand(
                "SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1",
                to
            )
        )
            Check(
                Convert.ToInt32(await check.ExecuteScalarAsync()) == 0,
                "all FK enabled and trusted"
            );
        await using (var check = new SqlCommand("DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS", to))
        await using (var reader = await check.ExecuteReaderAsync())
            Check(!await reader.ReadAsync(), "all check constraints");
        foreach (var login in fixture.Logins)
            Check(
                await new AuthenticationService(db).ValidateCredentialsAsync(
                    login.Email,
                    DemoFixture.Password
                )
                    is not null,
                "login " + login.Email
            );
        var importedFixture = new DemoFixture(db);
        importedFixture.Clock.Value = DateTime.UtcNow;
        await new SellerIdentityHasher(importedFixture.Config).EnsureKeyAsync(db, default);
        var registry = new SellerRegistrationService(
            db,
            new SellerIdentityHasher(importedFixture.Config),
            importedFixture.Config,
            new DemoEnvironment(),
            importedFixture.Clock
        );
        var denied = false;
        var writeoff = fixture.Logins.Single(x => x.Email == "seller.writeoff@demo.test");
        try
        {
            await registry.RegisterAsync(
                new(
                    "Blacklist probe",
                    "blocked-probe@demo.test",
                    DemoFixture.Password,
                    DemoFixture.Password,
                    "Blocked probe",
                    new("CCCD", writeoff.Identity!),
                    "qa-blocked-probe"
                )
            );
        }
        catch (UnauthorizedAccessException)
        {
            denied = true;
        }
        Check(
            denied && !await db.Users.AnyAsync(x => x.Email == "blocked-probe@demo.test"),
            "actual blacklist blocks registration"
        );
        var accounts = await db.SellerAccounts.AsNoTracking().ToListAsync();
        foreach (var account in accounts)
        {
            Check(
                account.ProcessingBalance >= 0
                    && account.AvailableBalance >= 0
                    && account.OnHoldBalance >= 0
                    && account.NegativeBalance >= 0,
                "nonnegative wallet " + account.SellerId
            );
            var processing = await db
                .SellerSettlements.Where(x => x.SellerAccountId == account.Id)
                .SumAsync(x => x.ProcessingAmount);
            Check(
                processing == account.ProcessingBalance,
                "processing reconciles " + account.SellerId
            );
            var month = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var sales = await (
                from p in db.Payments
                join o in db.OrderTables on p.OrderId equals o.Id
                where o.SellerId == account.SellerId && p.Status == "Succeeded" && p.PaidAt >= month
                select o.SellerGrossSnapshot
            ).SumAsync();
            Check(
                sales == account.MonthlySalesAmount,
                "monthly sales reconciles " + account.SellerId
            );
            await importedFixture.Finance.GetSummaryAsync(account.SellerId);
            await importedFixture.Finance.GetDebtAsync(account.SellerId);
            await importedFixture.Finance.GetLevelDetailsAsync(account.SellerId);
        }
        var seller2 = await db.Users.SingleAsync(x => x.Email == "seller.l2@demo.test");
        var seller3 = await db.Users.SingleAsync(x => x.Email == "seller.l3@demo.test");
        Check(accounts.Single(x => x.SellerId == seller2.Id).Level == 2, "seller level 2");
        Check(accounts.Single(x => x.SellerId == seller3.Id).Level == 3, "seller level 3");
        var recovered = await db.Users.SingleAsync(x => x.Email == "seller.recovered@demo.test");
        Check(
            accounts.Single(x => x.SellerId == recovered.Id).NegativeBalance == 0
                && accounts.Single(x => x.SellerId == recovered.Id).PermanentSalesBlockedAt != null,
            "recovery retains permanent block"
        );
        var buyer = fixture.Buyer.Id;
        foreach (var dispute in await db.Disputes.AsNoTracking().ToListAsync())
            await importedFixture.Disputes.GetDetailAsync(dispute.Id, buyer, "buyer");
        foreach (var c in fixture.Cases.Where(x => x.Type == "Order"))
            Check(
                await db.OrderTables.AnyAsync(x => x.Id == c.Id),
                "scenario order exists " + c.Code
            );
        Console.WriteLine(
            $"Validated {fixture.Logins.Count} logins, {accounts.Count} wallets, blacklist and {fixture.Cases.Count} cases."
        );
    }

    private static void Check(bool ok, string message)
    {
        if (!ok)
            throw new InvalidOperationException("Demo validation failed: " + message);
    }
}
