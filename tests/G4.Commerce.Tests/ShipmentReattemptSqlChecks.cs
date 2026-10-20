using System.Text.RegularExpressions;
using G4.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

internal static class ShipmentReattemptSqlChecks
{
    private const string QaName = "G4_ShippingReattempt_Test_20261020";
    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("G4_SHIPPING_REATTEMPT_SQL_CONNECTION") ?? throw new Exception("Dedicated SH1 QA connection required");
        if (new SqlConnectionStringBuilder(connection).InitialCatalog != QaName) throw new Exception("SH1 SQL checks only accept " + QaName);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        await Prepare(options, connection);
        foreach (var direction in new[] { "Outbound", "Return" }) await ConcurrentEvents(options, direction);
        await Rollback(options);
        Console.WriteLine("Real SQL SH1 checks passed: additive migration retains legacy actors/history, both directions, concurrent schedule/start once, claimability, actor FK and event/outbox/realtime rollback");
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
            var baseline = Regex.Replace(await File.ReadAllTextAsync("database/baseline.sql"), @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE CloneEbayDB;)\s*$", "");
            foreach (var batch in Regex.Split(baseline, @"(?im)^\s*GO\s*$"))
                if (!string.IsNullOrWhiteSpace(batch)) { await using var cmd = new SqlCommand(batch, sql); await cmd.ExecuteNonQueryAsync(); }
            await db.Database.GetService<IMigrator>().MigrateAsync("20261017050657_CompleteBusinessNotifications");
            // Seed using the old schema so migration preservation is tested, not inferred.
            await using var legacy = new SqlCommand(@"
                INSERT INTO OrderTable ([status]) VALUES (N'Shipping');
                DECLARE @order int = SCOPE_IDENTITY();
                INSERT INTO ShippingInfo (orderId, [status], Direction, TrackingNumber, CreatedAt, DeliveryAttempts)
                VALUES (@order, N'DeliveryFailed', N'Outbound', N'SH1-LEGACY', '2026-10-19T00:00:00', 1);
                DECLARE @parcel int = SCOPE_IDENTITY();
                INSERT INTO ShippingEvent (ShippingInfoId, ExternalEventId, [Status], OccurredAt, ReceivedAt, Note)
                VALUES (@parcel, N'sh1-legacy-event', N'DeliveryFailed', '2026-10-19T00:00:00', '2026-10-19T00:00:00', N'Legacy note');", sql);
            await legacy.ExecuteNonQueryAsync();
        }
        await db.Database.MigrateAsync();
        var old = await db.ShippingEvents.AsNoTracking().SingleAsync(x => x.ExternalEventId == "sh1-legacy-event");
        Check(old.ActorUserId == null && old.ActorRole == null && old.ReattemptAt == null && old.Note == "Legacy note"
            && old.OccurredAt == new DateTime(2026, 10, 19), "migration must not guess actors or rewrite old event snapshots");
        Check((await db.ShippingInfos.AsNoTracking().SingleAsync(x => x.TrackingNumber == "SH1-LEGACY")).DeliveryAttempts == 1, "legacy attempts preserved");
    }

    private static async Task ConcurrentEvents(DbContextOptions<ApplicationDbContext> options, string direction)
    {
        (int Order, int Shipment, int Shipper, int OtherShipper) f;
        await using (var db = new ApplicationDbContext(options))
        {
            f = await ShipmentReattemptChecks.Seed(db, direction);
            await ShipmentReattemptChecks.Failed(new ShipmentService(db, new ReturnAutomationChecks.Carrier()), f.Shipment, f.Shipper);
        }
        var key = "sql-schedule-" + f.Shipment;
        var at = DateTimeOffset.UtcNow.AddHours(1).ToOffset(TimeSpan.FromHours(7));
        async Task<bool> Record(string id, string status, DateTimeOffset? date = null)
        {
            await using var db = new ApplicationDbContext(options);
            return await new ShipmentService(db, new ReturnAutomationChecks.Carrier()).RecordEventAsync(f.Shipment, id, status,
                note: date != null ? "Recipient rescheduled" : null, shipperId: f.Shipper, reattemptAt: date);
        }
        var scheduled = await Task.WhenAll(Record(key, "ReattemptScheduled", at), Record(key, "ReattemptScheduled", at));
        Check(scheduled.Count(x => x) == 1, "concurrent duplicate schedules append once");
        await using (var db = new ApplicationDbContext(options))
        {
            var p = await db.ShippingInfos.SingleAsync(x => x.Id == f.Shipment);
            Check(p.Status == "ReattemptScheduled" && p.DeliveryAttempts == 1 && p.ReattemptAt == at && p.ReattemptAt.Value.Offset == TimeSpan.Zero,
                "SQL schedule stores UTC and does not spend another attempt");
            var e = await db.ShippingEvents.SingleAsync(x => x.ExternalEventId == key);
            Check(e.ActorUserId == f.Shipper && e.ActorRole == "shipper" && e.ReattemptAt == at, "SQL event preserves actor and schedule snapshot");
            Check(await db.NotificationOutbox.CountAsync(x => x.OrderId == f.Order && x.EventType == "ReattemptScheduled") == 2, "retry cannot duplicate recipient notifications");
            p.ShipperId = null; await db.SaveChangesAsync();
            var page = await ShipperShipmentList.ReadAsync(db, f.OtherShipper, "pending", 1, 50, search: "#" + f.Order, status: "ReattemptScheduled", direction: direction);
            Check(page.Items.Single().ReattemptAt == at, "scheduled unassigned legacy shipment remains claimable and filterable");
            await new ShipmentService(db, new ReturnAutomationChecks.Carrier()).ClaimAsync(p.Id, f.Shipper);
        }
        var started = await Task.WhenAll(Record("sql-start-" + f.Shipment, "OutForDelivery"), Record("sql-start-" + f.Shipment, "OutForDelivery"));
        Check(started.Count(x => x) == 1, "concurrent duplicate starts consume one attempt");
        await using var verify = new ApplicationDbContext(options);
        Check((await verify.ShippingInfos.SingleAsync(x => x.Id == f.Shipment)).DeliveryAttempts == 2, "counter is exactly two");
        var service = new ShipmentService(verify, new ReturnAutomationChecks.Carrier());
        await service.RecordEventAsync(f.Shipment, "sql-failed-two-" + f.Shipment, "DeliveryFailed", shipperId: f.Shipper);
        Check(!await service.RecordEventAsync(f.Shipment, "sql-third-" + f.Shipment, "ReattemptScheduled", note: "third", shipperId: f.Shipper, reattemptAt: at), "SQL cannot schedule a third attempt");
        await ShipmentReattemptChecks.Reject<InvalidOperationException>(() => service.RecordEventAsync(f.Shipment, key, "ReattemptScheduled", note: "different", shipperId: f.Shipper, reattemptAt: at));
        await ShipmentReattemptChecks.Reject<UnauthorizedAccessException>(() => service.RecordEventAsync(f.Shipment, key, "ReattemptScheduled", note: "Recipient rescheduled", shipperId: f.OtherShipper, reattemptAt: at));
    }

    private static async Task Rollback(DbContextOptions<ApplicationDbContext> options)
    {
        (int Order, int Shipment, int Shipper, int OtherShipper) f;
        int beforeSignals;
        await using (var db = new ApplicationDbContext(options))
        {
            f = await ShipmentReattemptChecks.Seed(db);
            await ShipmentReattemptChecks.Failed(new ShipmentService(db, new ReturnAutomationChecks.Carrier()), f.Shipment, f.Shipper);
            beforeSignals = await db.CommerceChanges.CountAsync();
        }
        var failing = new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(new FailAfterScheduleSave()).Options;
        await using (var db = new ApplicationDbContext(failing))
            await ShipmentReattemptChecks.Reject<InvalidOperationException>(() => new ShipmentService(db, new ReturnAutomationChecks.Carrier()).RecordEventAsync(f.Shipment,
                "sql-rollback-" + f.Shipment, "ReattemptScheduled", note: "reason", shipperId: f.Shipper, reattemptAt: DateTimeOffset.UtcNow.AddHours(1)));
        await using var verify = new ApplicationDbContext(options);
        var p = await verify.ShippingInfos.SingleAsync(x => x.Id == f.Shipment);
        Check(p.Status == "DeliveryFailed" && p.ReattemptAt == null && p.DeliveryAttempts == 1, "transaction rollback preserves shipment and attempt count");
        Check(!await verify.ShippingEvents.AnyAsync(x => x.ShippingInfoId == f.Shipment && x.Status == "ReattemptScheduled")
            && !await verify.NotificationOutbox.AnyAsync(x => x.OrderId == f.Order && x.EventType == "ReattemptScheduled"), "event and notifications roll back together");
        Check(await verify.CommerceChanges.CountAsync() == beforeSignals, "rolled-back schedule emits no realtime change");
    }

    private sealed class FailAfterScheduleSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<ShippingEvent>().Any(x => x.Entity.Status == "ReattemptScheduled"))
                throw new InvalidOperationException("Simulated failure after saving schedule and outbox, before commit");
            return ValueTask.FromResult(result);
        }
    }
    private static void Check(bool value, string message) => ShipmentReattemptChecks.Check(value, message);
}
