using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Checkout;

public sealed class ShipmentService(ApplicationDbContext db, ICarrierGateway carrier)
{
    public async Task<ShippingInfo> CreateOutboundAsync(int orderId, CancellationToken ct = default)
    {
        var existing = await db.ShippingInfos.SingleOrDefaultAsync(s => s.OrderId == orderId && s.Direction == "Outbound", ct);
        if (existing is not null && existing.TrackingNumber is not null) return existing;
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        if (order.Status is not ("Paid" or "Preparing")) throw new InvalidOperationException("Order is not ready to ship");
        var key = $"ship-{orderId}-outbound";
        var shipment = existing ?? new ShippingInfo
        {
            OrderId = orderId, Direction = "Outbound", Status = "NotCreated",
            IdempotencyKey = key, CreatedAt = DateTime.UtcNow, Carrier = "G4 Demo Carrier"
        };
        if (existing is null) db.ShippingInfos.Add(shipment);
        await db.SaveChangesAsync(ct);
        try
        {
            shipment.TrackingNumber = await RetryLabelAsync(orderId, "Outbound", key, ct);
            shipment.Status = ShippingState.Next("NotCreated", "LabelCreated");
            order.Status = "Shipping";
            db.ShippingEvents.Add(new ShippingEvent
            {
                ShippingInfoId = shipment.Id, ExternalEventId = $"{key}-label", Status = "LabelCreated",
                OccurredAt = DateTime.UtcNow, ReceivedAt = DateTime.UtcNow, Note = "Label created"
            });
        }
        catch (HttpRequestException)
        {
            shipment.Status = "ShipmentCreationFailed";
        }
        await db.SaveChangesAsync(ct);
        return shipment;
    }

    public async Task<ShippingInfo> CreateReturnAsync(int orderId, CancellationToken ct = default)
    {
        var existing = await db.ShippingInfos.SingleOrDefaultAsync(s => s.OrderId == orderId && s.Direction == "Return", ct);
        if (existing is not null && existing.TrackingNumber is not null) return existing;
        var key = $"ship-{orderId}-return";
        var shipment = existing ?? new ShippingInfo
        {
            OrderId = orderId, Direction = "Return", Status = "NotCreated", IdempotencyKey = key,
            CreatedAt = DateTime.UtcNow, Carrier = "G4 Demo Carrier"
        };
        if (existing is null) db.ShippingInfos.Add(shipment);
        await db.SaveChangesAsync(ct);
        try
        {
            shipment.TrackingNumber = await RetryLabelAsync(orderId, "Return", key, ct);
            shipment.Status = "LabelCreated";
            db.ShippingEvents.Add(new ShippingEvent
            {
                ShippingInfoId = shipment.Id, ExternalEventId = $"{key}-label", Status = "LabelCreated",
                OccurredAt = DateTime.UtcNow, ReceivedAt = DateTime.UtcNow, Note = "Return label created"
            });
        }
        catch (HttpRequestException)
        {
            shipment.Status = "ShipmentCreationFailed";
        }
        await db.SaveChangesAsync(ct);
        return shipment;
    }

    public async Task<bool> RecordEventAsync(int shippingInfoId, string eventId, string nextStatus,
        string? location = null, string? note = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 100) throw new ArgumentException("Event ID required");
        if (await db.ShippingEvents.AnyAsync(e => e.ExternalEventId == eventId, ct)) return false;
        var shipment = await db.ShippingInfos.SingleAsync(s => s.Id == shippingInfoId, ct);
        if (shipment.Status is null || !ShippingState.TryNext(shipment.Status, nextStatus, out var status)) return false;
        if (status == "OutForDelivery" && shipment.DeliveryAttempts >= 2) return false;
        shipment.Status = status;
        if (status == "OutForDelivery") shipment.DeliveryAttempts++;
        if (status == "Delivered") shipment.DeliveredAt = DateTime.UtcNow;
        if (status == "DeliveryFailed") shipment.FailureReason = note;
        db.ShippingEvents.Add(new ShippingEvent
        {
            ShippingInfoId = shippingInfoId, ExternalEventId = eventId, Status = status,
            Location = location, Note = note, OccurredAt = DateTime.UtcNow, ReceivedAt = DateTime.UtcNow
        });
        if (shipment.Direction == "Outbound" && status == "Delivered")
        {
            var order = await db.OrderTables.SingleAsync(o => o.Id == shipment.OrderId, ct);
            order.Status = OrderState.Next(order.Status!, "Deliver");
            await new MvpService(db).QueueEmailAsync(order, "Delivered", "Order delivered", $"Order {order.Id} was delivered.", ct);
        }
        if (shipment.Direction == "Outbound" && status == "DeliveryFailed")
        {
            var order = await db.OrderTables.SingleAsync(o => o.Id == shipment.OrderId, ct);
            await new MvpService(db).QueueEmailAsync(order, "DeliveryFailed", "Delivery failed", $"Delivery of order {order.Id} failed.", ct);
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<string> RetryLabelAsync(int orderId, string direction, string key, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try { return await carrier.CreateLabelAsync(orderId, direction, key, ct); }
            catch (HttpRequestException) when (attempt < 3) { await Task.Delay(50 * (1 << (attempt - 1)), ct); }
        }
        throw new HttpRequestException("Carrier unavailable after three attempts");
    }
}
