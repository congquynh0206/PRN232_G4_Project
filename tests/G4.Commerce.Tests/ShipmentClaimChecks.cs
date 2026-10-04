using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;

internal static class ShipmentClaimChecks
{
    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.AddRange(new OrderTable { Id = 1, Status = "Shipping" },
            new OrderTable { Id = 2, Status = "Closed" },
            new User { Id = 1, Role = "shipper" }, new User { Id = 2, Role = "shipper" }, new User { Id = 3, Role = "buyer" },
            new ShippingInfo { Id = 1, OrderId = 1, Direction = "Outbound", Status = "LabelCreated", TrackingNumber = "OUT-1" },
            new ShippingInfo { Id = 2, OrderId = 1, Direction = "Return", Status = "LabelCreated", TrackingNumber = "RET-1" },
            new ShippingInfo { Id = 3, OrderId = 1, Direction = "Outbound", Status = "InTransit", TrackingNumber = "LEGACY" },
            new ShippingInfo { Id = 4, OrderId = 2, Direction = "Outbound", Status = "LabelCreated", TrackingNumber = "CLOSED" });
        await db.SaveChangesAsync();
        var service = new ShipmentService(db, new ReturnAutomationChecks.Carrier());
        var rejected = false;
        try { await service.RecordEventAsync(1, "unclaimed-event", "PickedUp"); }
        catch (UnauthorizedAccessException) { rejected = true; }
        ReturnAutomationChecks.Check(rejected, "tracking without shipment claim must be rejected");
        ReturnAutomationChecks.Check(!await db.ShippingEvents.AnyAsync(), "unauthorized tracking writes no events");
        var first = await service.ClaimAsync(1, 1);
        var repeat = await service.ClaimAsync(1, 1);
        ReturnAutomationChecks.Check(first.ClaimedAt == repeat.ClaimedAt && first.ShipperId == 1, "claim owner is idempotent");
        await Reject<InvalidOperationException>(() => service.ClaimAsync(1, 2));
        await Reject<UnauthorizedAccessException>(() => service.ClaimAsync(2, 3));
        await Reject<InvalidOperationException>(() => service.ClaimAsync(4, 1));
        await service.RecordEventAsync(1, "pickup", "PickedUp", shipperId: 1);
        await Reject<UnauthorizedAccessException>(() => service.RecordEventAsync(1, "pickup", "PickedUp", shipperId: 2));
        await service.ClaimAsync(2, 2);
        var mine = await G4.Infrastructure.Shipping.ShipperShipmentList.ReadAsync(db, 1, "mine", 1, 10);
        ReturnAutomationChecks.Check(mine.Items.Count == 1 && mine.Items[0].Id == 1, "shipper sees only owned shipments");
        var pending = await G4.Infrastructure.Shipping.ShipperShipmentList.ReadAsync(db, 1, "pending", 1, 10);
        ReturnAutomationChecks.Check(pending.Items.Count == 1 && pending.Items[0].Id == 3, "legacy unassigned active shipments can be claimed");
        await service.ClaimAsync(3, 1);
        ReturnAutomationChecks.Check((await db.ShippingInfos.SingleAsync(x => x.Id == 3)).Status == "InTransit", "legacy claim does not reset tracking");
        var searched = await G4.Infrastructure.Shipping.ShipperShipmentList.ReadAsync(db, 1, "mine", 1, 1, search: "LEGACY", status: "InTransit", direction: "Outbound");
        ReturnAutomationChecks.Check(searched.TotalCount == 1 && searched.Items.Single().Id == 3 && searched.MineCount == 2, "search and filters apply before pagination without altering tab totals");
        var foreign = await G4.Infrastructure.Shipping.ShipperShipmentList.ReadAsync(db, 1, "mine", 1, 10, search: "RET-1");
        ReturnAutomationChecks.Check(foreign.TotalCount == 0, "search cannot expose another shipper's shipments");
        var orderSearch = await G4.Infrastructure.Shipping.ShipperShipmentList.ReadAsync(db, 1, "mine", 99, 1, search: "#1");
        ReturnAutomationChecks.Check(orderSearch.TotalCount == 2 && orderSearch.Page == 2 && orderSearch.Items.Single().Id == 1, "order search and clamped pagination compose");
        var capture = new CaptureSellerSql();
        await using var sqlDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=unused;Database=translation-test;Integrated Security=true")
            .AddInterceptors(new SuppressSellerSqlConnection(), capture).Options);
        await G4.Infrastructure.Shipping.ShipperShipmentList.ReadAsync(sqlDb, 1, "pending", 1, 10);
        await G4.Infrastructure.Shipping.ShipperShipmentList.ReadAsync(sqlDb, 1, "mine", 1, 10);
        ReturnAutomationChecks.Check(capture.Commands == 6, "SQL Server translates both shipment list filters");
        await G4.Infrastructure.Shipping.ShipperShipmentList.ReadAsync(sqlDb, 1, "mine", 1, 10, search: "#1", status: "PickedUp", direction: "Outbound");
        ReturnAutomationChecks.Check(capture.Commands == 10, "SQL Server translates combined search and status filters");
        Console.WriteLine("Shipper ownership checks passed");
    }

    private static async Task Reject<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
}
