using System.Text.RegularExpressions;
using G4.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

internal static class ImmediateCheckoutSqlChecks
{
    private const string DatabaseName = "G4_ImmediateCheckout_Test_20261108";

    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("G4_IMMEDIATE_SQL_CONNECTION")
            ?? throw new Exception("Set G4_IMMEDIATE_SQL_CONNECTION to the dedicated QA database.");
        if (new SqlConnectionStringBuilder(connection).InitialCatalog != DatabaseName)
            throw new Exception("Immediate checkout SQL checks only accept " + DatabaseName);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection).Options;
        await Prepare(options, connection);
        await Compete(options, buyers: 2, stock: 1, sameSession: false);
        await Compete(options, buyers: 20, stock: 5, sameSession: false);
        await Compete(options, buyers: 10, stock: 1, sameSession: true);
        await Rollback(options, connection);
        Console.WriteLine("SQL immediate checkout passed: 2/1, 20/5, duplicate submits, rollback/retry.");
    }

    private static async Task Prepare(
        DbContextOptions<ApplicationDbContext> options, string connection
    )
    {
        await using var db = new ApplicationDbContext(options);
        var creator = db.Database.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync())
            await creator.CreateAsync();
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync();
        await using var count = new SqlCommand(
            "SELECT COUNT(*) FROM sys.tables WHERE name <> '__EFMigrationsHistory'", sql);
        if (Convert.ToInt32(await count.ExecuteScalarAsync()) == 0)
        {
            var baseline = await File.ReadAllTextAsync("database/baseline.sql");
            baseline = Regex.Replace(baseline,
                @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE CloneEbayDB;)\s*$", "");
            foreach (var batch in Regex.Split(baseline, @"(?im)^\s*GO\s*$"))
            {
                if (string.IsNullOrWhiteSpace(batch))
                    continue;
                await using var command = new SqlCommand(batch, sql);
                await command.ExecuteNonQueryAsync();
            }
        }
        await db.Database.MigrateAsync();
    }

    private static async Task Compete(
        DbContextOptions<ApplicationDbContext> options, int buyers, int stock, bool sameSession
    )
    {
        int product;
        var sessions = new List<(Guid Id, int Buyer)>();
        await using (var db = new ApplicationDbContext(options))
        {
            var f = await ImmediateCheckoutChecks.Seed(db, stock);
            product = f.Product;
            for (var index = 0; index < buyers; index++)
            {
                var buyerId = f.Buyer;
                var addressId = f.Address;
                if (index > 0 && !sameSession)
                {
                    var buyer = new User
                    {
                        Role = "buyer", Email = Guid.NewGuid() + "@example.test",
                    };
                    db.Users.Add(buyer);
                    await db.SaveChangesAsync();
                    var address = new Address
                    {
                        UserId = buyer.Id, State = "Hanoi", Country = "Vietnam",
                    };
                    db.Addresses.Add(address);
                    await db.SaveChangesAsync();
                    buyerId = buyer.Id;
                    addressId = address.Id;
                }
                sessions.Add((sameSession && index > 0 ? sessions[0].Id
                    : await ImmediateCheckoutChecks.Start(db, product, buyerId, addressId), buyerId));
            }
            ImmediateCheckoutChecks.Check(
                (await db.Inventories.SingleAsync(i => i.ProductId == product)).Quantity == stock,
                "all SQL sessions opened without reservation");
        }
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var authorized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var authorizationCount = 0;
        var provider = new G4.Infrastructure.Payments.SimulatedImmediateCardGateway(
            new ImmediateCheckoutChecks.ContextFactory(options));
        var commonKey = Guid.NewGuid().ToString();
        var tasks = sessions.Select(async session =>
        {
            await start.Task;
            await using var db = new ApplicationDbContext(options);
            var gateway = new ImmediateAuthorizationChecks.FaultGateway(provider)
            {
                CheckNoTransaction = () => ImmediateCheckoutChecks.Check(
                    db.Database.CurrentTransaction == null, "gateway outside SQL transaction"),
                AfterAuthorization = async () =>
                {
                    if (Interlocked.Increment(ref authorizationCount) == (sameSession ? 1 : buyers))
                        authorized.TrySetResult();
                    await authorized.Task.WaitAsync(TimeSpan.FromSeconds(45));
                },
            };
            try
            {
                return (await ImmediateAuthorizationChecks.Service(db, gateway).PayCardAsync(
                    session.Buyer, session.Id,
                    new("4111111111111111", "12/30",
                        sameSession ? commonKey : Guid.NewGuid().ToString()))).OrderId;
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("stock", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(tasks);
        await using var check = new ApplicationDbContext(options);
        var paid = await check.OrderTables
            .Where(o => o.OrderItems.Any(i => i.ProductId == product)).ToListAsync();
        ImmediateCheckoutChecks.Check(paid.Count == stock && paid.All(o => o.Status == "Paid"),
            "exactly stock-count paid orders, no unpaid orphan");
        ImmediateCheckoutChecks.Check(results.Where(x => x != null).Distinct().Count() == stock,
            "only inventory winners succeed; duplicate requests replay one order");
        ImmediateCheckoutChecks.Check(
            (await check.Inventories.SingleAsync(i => i.ProductId == product)).Quantity == 0,
            "SQL inventory never oversells");
        var ids = paid.Select(o => o.Id).ToArray();
        ImmediateCheckoutChecks.Check(await check.Payments.CountAsync(p => ids.Contains(p.OrderId ?? 0)
            && p.Status == "Succeeded") == stock, "exactly one capture per paid order");
        ImmediateCheckoutChecks.Check(await check.SellerSettlements.CountAsync(
            s => ids.Contains(s.OrderId)) == stock, "exactly one settlement per paid order");
        var sessionIds = sessions.Select(s => s.Id).ToArray();
        var attempts = await check.ImmediatePaymentAttempts.AsNoTracking()
            .Where(a => sessionIds.Contains(a.CheckoutId)).ToListAsync();
        var providerKeys = attempts.Select(a => "IP-" + a.Id).ToArray();
        var bank = await check.SimulatedCardAuthorizations.AsNoTracking()
            .Where(a => providerKeys.Contains(a.Id)).ToListAsync();
        ImmediateCheckoutChecks.Check(bank.Count(a => a.Status == "Captured") == stock,
            "only inventory winners capture");
        if (!sameSession)
        {
            ImmediateCheckoutChecks.Check(bank.Count(a => a.Status == "Voided") == buyers - stock
                && bank.Where(a => a.Status == "Voided").All(a => a.CaptureId == null),
                "every authorized loser is voided and never captured");
        }
    }

    private static async Task Rollback(
        DbContextOptions<ApplicationDbContext> options, string connection
    )
    {
        (int Product, int Buyer, int Address, int Seller) f;
        Guid session;
        await using (var db = new ApplicationDbContext(options))
        {
            f = await ImmediateCheckoutChecks.Seed(db, 1);
            session = await ImmediateCheckoutChecks.Start(db, f.Product, f.Buyer, f.Address);
        }
        var failOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection).AddInterceptors(new FailFinalSave()).Options;
        await using (var db = new ApplicationDbContext(failOptions))
        {
            var verifying = await ImmediateCheckoutChecks.Service(db).PayCardAsync(f.Buyer, session,
                new("4111111111111111", "12/30", session.ToString()));
            ImmediateCheckoutChecks.Check(verifying.Status == "Verifying"
                && verifying.Stage == "Captured", "captured payment is recoverable after DB failure");
        }
        await using var check = new ApplicationDbContext(options);
        ImmediateCheckoutChecks.Check(
            (await check.Inventories.SingleAsync(i => i.ProductId == f.Product)).Quantity == 0
            && !await check.OrderTables.AnyAsync(o => o.OrderItems.Any(i => i.ProductId == f.Product))
            && await check.ImmediatePaymentAttempts.AnyAsync(a => a.CheckoutId == session
                && a.InventoryHeld && a.CaptureId != null),
            "order transaction rolls back while durable capture and claim remain for recovery");
        var retry = await ImmediateCheckoutChecks.Service(check).PayCardAsync(f.Buyer, session,
            new("4111111111111111", "12/30", session.ToString()));
        ImmediateCheckoutChecks.Check(retry.Status == "Succeeded", "same key reconciles after rollback");
        ImmediateCheckoutChecks.Check(await check.Payments.CountAsync(p => p.OrderId == retry.OrderId)
            == 1, "recovery creates one payment only");
    }

    private sealed class FailFinalSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data, InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (data.Context!.ChangeTracker.Entries<ImmediatePaymentAttempt>()
                .Any(e => e.State == EntityState.Modified && e.Entity.Status == "Succeeded"))
                throw new DbUpdateException("Injected final save failure");
            return base.SavingChangesAsync(data, result, cancellationToken);
        }
    }
}
