using System.Text.RegularExpressions;
using G4.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

internal static class PayPalReservationSqlChecks
{
    private const string DatabaseName = "G4_PayPalReservation_Test_20261019";

    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("G4_PAYPAL_RESERVATION_SQL_CONNECTION")
            ?? throw new Exception("Dedicated PayPal reservation SQL QA connection required");
        if (new SqlConnectionStringBuilder(connection).InitialCatalog != DatabaseName)
            throw new Exception("Reservation SQL checks only accept " + DatabaseName);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        await Prepare(options, connection);
        await CompetingCaptures(options);
        await RollbackAndReconcile(options);
        await TerminalRace(options, "DENIED");
        await TerminalRace(options, "COMPLETED");
        Console.WriteLine("Real SQL PayPal reservation checks passed: competing orders/card, committed preflight, released locks before provider, rollback/reconcile and terminal race; only dedicated QA database used");
    }

    private static async Task Prepare(DbContextOptions<ApplicationDbContext> options, string connection)
    {
        await using var db = new ApplicationDbContext(options);
        var creator = db.Database.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync()) await creator.CreateAsync();
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync();
        await using var count = new SqlCommand("SELECT COUNT(*) FROM sys.tables WHERE name <> '__EFMigrationsHistory'", sql);
        if (Convert.ToInt32(await count.ExecuteScalarAsync()) == 0)
        {
            // Only a completely empty, explicitly guarded QA database is bootstrapped.
            var baseline = await File.ReadAllTextAsync("database/baseline.sql");
            baseline = Regex.Replace(baseline, @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE CloneEbayDB;)\s*$", "");
            foreach (var batch in Regex.Split(baseline, @"(?im)^\s*GO\s*$"))
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    await using var command = new SqlCommand(batch, sql);
                    await command.ExecuteNonQueryAsync();
                }
        }
        await db.Database.MigrateAsync();
    }

    private static async Task CompetingCaptures(DbContextOptions<ApplicationDbContext> options)
    {
        var gateway = new PayPalReservationChecks.Gateway();
        int first, second, seller;
        string firstProvider, secondProvider;
        await using (var db = new ApplicationDbContext(options))
        {
            var f = await PayPalReservationChecks.Seed(db);
            (first, second, seller) = (f.First, f.Second, f.Seller);
            var pay = PayPalReservationChecks.Service(db, gateway);
            firstProvider = (await pay.StartAsync(first, Guid.NewGuid().ToString())).Id;
            secondProvider = (await pay.StartAsync(second, Guid.NewGuid().ToString())).Id;
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.BeforeCapture = async () => { entered.TrySetResult(); await release.Task; };
        async Task<string?> Capture(int orderId, string providerId)
        {
            await using var db = new ApplicationDbContext(options);
            try { return (await PayPalReservationChecks.Service(db, gateway).CaptureAsync(orderId, providerId)).Status; }
            catch (InvalidOperationException) { return null; }
        }
        var a = Capture(first, firstProvider);
        var b = Capture(second, secondProvider);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var rejected = await Task.WhenAny(a, b).WaitAsync(TimeSpan.FromSeconds(20));
            Check(await rejected == null && gateway.CaptureCalls == 1, "only one competing order may reach provider while the other is blocked by capacity");
            await using var check = new ApplicationDbContext(options);
            var rows = await check.Payments.AsNoTracking().Where(p => p.OrderId == first || p.OrderId == second).ToListAsync();
            var reserved = rows.Single(p => p.Status == "Verifying");
            Check(reserved.CaptureReservedAt != null && reserved.ReservedSellerGross == 100m,
                "another connection sees the committed reservation before provider completion");
            Check(rows.Single(p => p.Status == "Pending").CaptureReservedAt == null, "losing preflight cannot leave a reservation or Verifying state");
            var pendingOrder = rows.Single(p => p.Status == "Pending").OrderId!.Value;
            await PayPalReservationChecks.Reject(() => new CheckoutService(check, PayPalReservationChecks.Finance(check))
                .PayCardAsync(pendingOrder, "4111111111111111", "12/30", Guid.NewGuid().ToString()));
            Check((await check.SellerAccounts.AsNoTracking().SingleAsync(x => x.SellerId == seller)).MonthlySalesAmount == 0m,
                "reservation is not successful revenue before provider confirms");
        }
        finally { release.TrySetResult(); }
        var results = await Task.WhenAll(a, b);
        Check(results.Count(x => x == "Succeeded") == 1 && results.Count(x => x == null) == 1, "one success and one uncharged rejection");
        await using var verify = new ApplicationDbContext(options);
        Check((await verify.SellerAccounts.SingleAsync(x => x.SellerId == seller)).MonthlySalesAmount == 100m,
            "competing captures cannot exceed USD 150 with two USD 100 orders");
        Check(await verify.SellerSettlements.CountAsync(x => x.SellerId == seller) == 1, "one settlement after concurrent capture");
    }

    private static async Task RollbackAndReconcile(DbContextOptions<ApplicationDbContext> options)
    {
        var gateway = new PayPalReservationChecks.Gateway();
        int order, seller;
        string provider;
        await using (var db = new ApplicationDbContext(options))
        {
            var f = await PayPalReservationChecks.Seed(db);
            (order, seller) = (f.First, f.Seller);
            provider = (await PayPalReservationChecks.Service(db, gateway).StartAsync(order, Guid.NewGuid().ToString())).Id;
        }
        var failingOptions = new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(new FailSettlement()).Options;
        await using (var db = new ApplicationDbContext(failingOptions))
            await PayPalReservationChecks.Reject(() => PayPalReservationChecks.Service(db, gateway).CaptureAsync(order, provider));

        await using (var db = new ApplicationDbContext(options))
        {
            var p = await db.Payments.SingleAsync(x => x.OrderId == order);
            Check(p.Status == "Verifying" && p.ReservedSellerGross == 100m && p.CaptureReservedAt != null,
                "settlement rollback preserves the earlier committed reservation for already captured money");
            Check((await db.OrderTables.FindAsync(order))!.Status == "AwaitingPayment"
                && !await db.SellerSettlements.AnyAsync(x => x.OrderId == order)
                && !await db.NotificationOutbox.AnyAsync(x => x.OrderId == order), "failed settlement rolls back payment/order/outbox together");
            Check((await db.SellerAccounts.SingleAsync(x => x.SellerId == seller)).MonthlySalesAmount == 0m, "rollback cannot record revenue");
            var pay = PayPalReservationChecks.Service(db, gateway);
            Check((await pay.ReconcileAsync(order)).Status == "Succeeded", "reconcile finishes settlement without recapture");
            await pay.ReconcileAsync(order);
            Check(gateway.CaptureCalls == 1 && gateway.UniqueCaptures == 1 && await db.SellerSettlements.CountAsync(x => x.OrderId == order) == 1,
                "rollback/retry cannot duplicate provider capture or settlement");
        }
    }

    private static async Task TerminalRace(DbContextOptions<ApplicationDbContext> options, string providerStatus)
    {
        var gateway = new PayPalReservationChecks.Gateway { LoseResponse = true };
        int order;
        string provider;
        await using (var db = new ApplicationDbContext(options))
        {
            order = (await PayPalReservationChecks.Seed(db)).First;
            provider = (await PayPalReservationChecks.Service(db, gateway).StartAsync(order, Guid.NewGuid().ToString())).Id;
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.BeforeCapture = async () => { entered.TrySetResult(); await release.Task; };
        async Task<string?> Capture()
        {
            await using var db = new ApplicationDbContext(options);
            return (await PayPalReservationChecks.Service(db, gateway).CaptureAsync(order, provider)).Status;
        }
        var inflight = Capture();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            // A worker can finish while capture still waits for its response.
            // Its committed terminal result must survive the late timeout.
            gateway.Status = providerStatus;
            await using var db = new ApplicationDbContext(options);
            Check((await PayPalReservationChecks.Service(db, gateway).ReconcileAsync(order)).Status == (providerStatus == "DENIED" ? "Failed" : "Succeeded"),
                "worker can resolve the unknown attempt while request is in flight");
        }
        finally { release.TrySetResult(); }
        var expected = providerStatus == "DENIED" ? "Failed" : "Succeeded";
        Check(await inflight == expected, "late timeout cannot change a terminal result back to Verifying");
        await using var verify = new ApplicationDbContext(options);
        var eventType = providerStatus == "DENIED" ? "PaymentFailed" : "PaymentSucceeded";
        Check((await verify.Payments.SingleAsync(x => x.OrderId == order)).Status == expected
            && await verify.NotificationOutbox.CountAsync(x => x.OrderId == order && x.EventType == eventType) == 1,
            "terminal race ends capacity reservation once and emits one notification");
        Check(await verify.SellerSettlements.CountAsync(x => x.OrderId == order) == (providerStatus == "DENIED" ? 0 : 1),
            "terminal success settles only once, failure never settles");
    }

    private sealed class FailSettlement : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<SellerSettlement>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Simulated settlement write failure after provider capture");
            return ValueTask.FromResult(result);
        }
    }
    private static void Check(bool value, string message) => PayPalReservationChecks.Check(value, message);
}
