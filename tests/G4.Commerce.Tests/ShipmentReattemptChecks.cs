using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class ShipmentReattemptChecks
{
    public static async Task RunAsync()
    {
        foreach (var direction in new[] { "Outbound", "Return" })
        {
            await Flow(direction);
            await Exhaustion(direction);
        }
        Console.WriteLine(
            "Shipment reattempts: both directions, required schedule/reason, actor, retries, ownership, two-attempt cap, real reversal, notifications and return delivery isolation passed"
        );
    }

    private static async Task Flow(string direction)
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        var f = await Seed(db, direction);
        var clock = new Clock();
        var service = new ShipmentService(db, new ReturnAutomationChecks.Carrier(), clock: clock);
        await Failed(service, f.Shipment, f.Shipper);
        Check(
            !await service.RecordEventAsync(
                f.Shipment,
                "jump",
                "OutForDelivery",
                shipperId: f.Shipper
            ),
            "direct reattempt is forbidden"
        );
        var at = new DateTimeOffset(clock.Utc.AddHours(1)).ToOffset(TimeSpan.FromHours(7));
        await Reject<ArgumentException>(() =>
            service.RecordEventAsync(
                f.Shipment,
                "missing-time",
                "ReattemptScheduled",
                note: "reason",
                shipperId: f.Shipper
            )
        );
        await Reject<ArgumentException>(() =>
            service.RecordEventAsync(
                f.Shipment,
                "missing-reason",
                "ReattemptScheduled",
                shipperId: f.Shipper,
                reattemptAt: at
            )
        );
        await Reject<ArgumentException>(() =>
            service.RecordEventAsync(
                f.Shipment,
                "past",
                "ReattemptScheduled",
                note: "reason",
                shipperId: f.Shipper,
                reattemptAt: at.AddDays(-1)
            )
        );
        await Reject<UnauthorizedAccessException>(() =>
            service.RecordEventAsync(
                f.Shipment,
                "foreign",
                "ReattemptScheduled",
                note: "reason",
                shipperId: f.OtherShipper,
                reattemptAt: at
            )
        );
        const string reason = "Người nhận hẹn lại <script>test</script>";
        Check(
            await service.RecordEventAsync(
                f.Shipment,
                "scheduled",
                "ReattemptScheduled",
                "Hanoi",
                reason,
                shipperId: f.Shipper,
                reattemptAt: at
            ),
            "schedule accepted"
        );
        var parcel = await db.ShippingInfos.SingleAsync(x => x.Id == f.Shipment);
        Check(
            parcel.DeliveryAttempts == 1
                && parcel.Status == "ReattemptScheduled"
                && parcel.ReattemptAt == at
                && parcel.ReattemptAt.Value.Offset == TimeSpan.Zero
                && parcel.ReattemptReason == reason,
            "schedule stores UTC and does not consume attempt"
        );
        var entry = await db.ShippingEvents.SingleAsync(x => x.ExternalEventId == "scheduled");
        Check(
            entry.ActorUserId == f.Shipper
                && entry.ActorRole == "shipper"
                && entry.ReattemptAt == at
                && entry.OccurredAt == clock.Utc,
            "event stores authenticated actor and schedule snapshot"
        );
        clock.Advance(7200);
        Check(parcel.Status == "ReattemptScheduled", "time alone cannot start delivery");
        Check(
            !await service.RecordEventAsync(
                f.Shipment,
                "scheduled",
                "ReattemptScheduled",
                "Hanoi",
                reason,
                shipperId: f.Shipper,
                reattemptAt: at
            ),
            "identical retry remains idempotent after scheduled time has passed"
        );
        await Reject<InvalidOperationException>(() =>
            service.RecordEventAsync(
                f.Shipment,
                "scheduled",
                "ReattemptScheduled",
                "Hanoi",
                "changed",
                shipperId: f.Shipper,
                reattemptAt: at
            )
        );
        Check(
            await db.NotificationOutbox.CountAsync(x => x.EventType == "ReattemptScheduled") == 2,
            "one notification per buyer/seller"
        );
        Check(
            await db
                .NotificationOutbox.Where(x => x.EventType == "ReattemptScheduled")
                .AllAsync(x =>
                    x.EventKey != null
                    && x.RecipientUserId != null
                    && !(x.HtmlBody ?? "").Contains("<script>")
                ),
            "schedule mail is scoped and encoded"
        );
        Check(
            await service.RecordEventAsync(
                f.Shipment,
                "second-start",
                "OutForDelivery",
                shipperId: f.Shipper
            ),
            "scheduled reattempt can start"
        );
        Check(
            !await service.RecordEventAsync(
                f.Shipment,
                "second-start",
                "OutForDelivery",
                shipperId: f.Shipper
            )
                && parcel.DeliveryAttempts == 2,
            "repeat start consumes one attempt only"
        );
        Check(
            await service.RecordEventAsync(
                f.Shipment,
                "delivered",
                "Delivered",
                shipperId: f.Shipper
            ),
            "reattempt can deliver"
        );
        var order = await db.OrderTables.FindAsync(f.Order);
        Check(order!.Status == "Delivered", "return delivery preserves outbound order result");
        if (direction == "Return")
        {
            Check(
                !await db.ShippingInfos.AnyAsync(x =>
                    x.Direction == "Outbound" && x.DeliveredAt != null
                ),
                "return cannot create buyer delivery evidence"
            );
            Check(
                !await db.NotificationOutbox.AnyAsync(x => x.EventType == "Delivered"),
                "return cannot send buyer-delivered notification"
            );
            Check(
                (await db.ReturnRequests.SingleAsync()).ConfirmationDueAt == clock.Utc.AddHours(48),
                "return receipt deadline still starts from actual return delivery"
            );
        }
        Check(
            (await db.ShippingEvents.FirstAsync(x => x.Status == "LabelCreated")).ActorRole
                == "system",
            "new label event names its system actor"
        );
    }

    private static async Task Exhaustion(string direction)
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        var f = await Seed(db, direction);
        var service = new ShipmentService(db, new ReturnAutomationChecks.Carrier());
        await Failed(service, f.Shipment, f.Shipper);
        var at = DateTimeOffset.UtcNow.AddHours(1);
        await service.RecordEventAsync(
            f.Shipment,
            "schedule",
            "ReattemptScheduled",
            note: "Hẹn lại",
            shipperId: f.Shipper,
            reattemptAt: at
        );
        await service.RecordEventAsync(
            f.Shipment,
            "start-two",
            "OutForDelivery",
            shipperId: f.Shipper
        );
        await service.RecordEventAsync(
            f.Shipment,
            "failed-two",
            "DeliveryFailed",
            shipperId: f.Shipper
        );
        Check(
            !await service.RecordEventAsync(
                f.Shipment,
                "third-schedule",
                "ReattemptScheduled",
                note: "Third",
                shipperId: f.Shipper,
                reattemptAt: at
            ),
            "third schedule is refused"
        );
        Check(
            !await service.RecordEventAsync(
                f.Shipment,
                "third-start",
                "OutForDelivery",
                shipperId: f.Shipper
            ),
            "third attempt is refused"
        );
        var parcel = await db.ShippingInfos.FindAsync(f.Shipment);
        Check(
            parcel!.Status == "DeliveryFailed"
                && parcel.DeliveryAttempts == 2
                && parcel.DeliveredAt == null,
            "exhaustion does not invent returned/delivered event"
        );
        await service.RecordEventAsync(
            f.Shipment,
            "reversing",
            "ReturningToSender",
            shipperId: f.Shipper
        );
        await service.RecordEventAsync(
            f.Shipment,
            "returned",
            "ReturnedToSeller",
            shipperId: f.Shipper
        );
        Check(
            parcel.Status == "ReturnedToSeller" && parcel.DeliveredAt == null,
            "reversal follows actual events and is not buyer delivery"
        );
        Check(
            !await db.NotificationOutbox.AnyAsync(x => x.EventType == "Delivered"),
            "failed delivery never sends success notification"
        );
    }

    internal static async Task<(int Order, int Shipment, int Shipper, int OtherShipper)> Seed(
        ApplicationDbContext db,
        string direction = "Outbound"
    )
    {
        var token = Guid.NewGuid().ToString("N");
        var buyer = new User
        {
            Role = "buyer",
            Email = "reattempt-buyer-" + token + "@example.test",
        };
        var seller = new User
        {
            Role = "seller",
            Email = "reattempt-seller-" + token + "@example.test",
        };
        var shipper = new User
        {
            Role = "shipper",
            Email = "reattempt-shipper-" + token + "@example.test",
        };
        var other = new User
        {
            Role = "shipper",
            Email = "reattempt-other-" + token + "@example.test",
        };
        db.Users.AddRange(buyer, seller, shipper, other);
        await db.SaveChangesAsync();
        var order = new OrderTable
        {
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            Status = direction == "Return" ? "Delivered" : "Paid",
            Currency = "USD",
            TotalPrice = 100m,
            OrderDate = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.OrderTables.Add(order);
        await db.SaveChangesAsync();
        if (direction == "Return")
        {
            db.ReturnRequests.Add(
                new ReturnRequest
                {
                    OrderId = order.Id,
                    UserId = buyer.Id,
                    Status = "Approved",
                    CreatedAt = DateTime.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }
        var service = new ShipmentService(db, new ReturnAutomationChecks.Carrier());
        var shipment =
            direction == "Return"
                ? await service.CreateReturnAsync(order.Id)
                : await service.CreateOutboundAsync(order.Id);
        await service.ClaimAsync(shipment.Id, shipper.Id);
        return (order.Id, shipment.Id, shipper.Id, other.Id);
    }

    internal static async Task Failed(ShipmentService service, int shipment, int shipper)
    {
        foreach (
            var status in new[] { "PickedUp", "InTransit", "OutForDelivery", "DeliveryFailed" }
        )
            Check(
                await service.RecordEventAsync(
                    shipment,
                    shipment + ":first-" + status,
                    status,
                    shipperId: shipper
                ),
                "initial delivery event " + status
            );
    }

    private sealed class Clock : TimeProvider
    {
        public DateTime Utc = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        public override DateTimeOffset GetUtcNow() => new(Utc);

        public void Advance(int seconds) => Utc = Utc.AddSeconds(seconds);
    }

    internal static void Check(bool value, string message) =>
        ReturnAutomationChecks.Check(value, message);

    internal static async Task Reject<T>(Func<Task> work)
        where T : Exception
    {
        try
        {
            await work();
        }
        catch (T)
        {
            return;
        }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
