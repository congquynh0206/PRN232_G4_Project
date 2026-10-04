using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Shipping;

public sealed class ShipmentService(ApplicationDbContext db, ICarrierGateway carrier,
    IConfiguration? config = null, TimeProvider? clock = null) : IShipmentService
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
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
            IdempotencyKey = key, CreatedAt = DateTime.UtcNow, Carrier = "G4 Carrier Simulator",
            PickupAddressSnapshot = order.PickupAddressSnapshot, DeliveryAddressSnapshot = order.AddressSnapshot
        };
        if (existing is null) db.ShippingInfos.Add(shipment);
        await db.SaveChangesAsync(ct);
        try
        {
            shipment.TrackingNumber = await RetryLabelAsync(orderId, "Outbound", key, ct);
            shipment.Status = ShippingState.Next("NotCreated", "LabelCreated");
            order.Status = "Shipping";
            order.UpdatedAt = DateTime.UtcNow;
            db.ShippingEvents.Add(new ShippingEvent
            {
                ShippingInfoId = shipment.Id, ExternalEventId = $"{key}-label", Status = "LabelCreated",
                OccurredAt = DateTime.UtcNow, ReceivedAt = DateTime.UtcNow, Note = "Đã tạo vận đơn giao hàng"
            });
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            shipment.Status = "ShipmentCreationFailed";
            order.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return shipment;
    }

    public async Task<ShippingInfo> CreateReturnAsync(int orderId, CancellationToken ct = default)
    {
        var existing = await db.ShippingInfos.SingleOrDefaultAsync(s => s.OrderId == orderId && s.Direction == "Return", ct);
        if (existing is not null && existing.TrackingNumber is not null) return existing;
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        var key = $"ship-{orderId}-return";
        var shipment = existing ?? new ShippingInfo
        {
            OrderId = orderId, Direction = "Return", Status = "NotCreated", IdempotencyKey = key,
            CreatedAt = DateTime.UtcNow, Carrier = "G4 Carrier Simulator",
            PickupAddressSnapshot = order.AddressSnapshot, DeliveryAddressSnapshot = order.PickupAddressSnapshot
        };
        if (existing is null) db.ShippingInfos.Add(shipment);
        await db.SaveChangesAsync(ct);
        try
        {
            shipment.TrackingNumber = await RetryLabelAsync(orderId, "Return", key, ct);
            shipment.Status = "LabelCreated";
            order.UpdatedAt = DateTime.UtcNow;
            db.ShippingEvents.Add(new ShippingEvent
            {
                ShippingInfoId = shipment.Id, ExternalEventId = $"{key}-label", Status = "LabelCreated",
                OccurredAt = DateTime.UtcNow, ReceivedAt = DateTime.UtcNow, Note = "Đã tạo vận đơn trả hàng"
            });
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            shipment.Status = "ShipmentCreationFailed";
            order.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return shipment;
    }

    public async Task<bool> RecordEventAsync(int shippingInfoId, string eventId, string nextStatus,
        string? location = null, string? note = null, CancellationToken ct = default, int? shipperId = null)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var shipment = db.Database.IsSqlServer()
            ? await db.ShippingInfos.FromSqlInterpolated($"SELECT * FROM [ShippingInfo] WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {shippingInfoId}").SingleAsync(ct)
            : await db.ShippingInfos.SingleAsync(s => s.Id == shippingInfoId, ct);
        if (db.Database.IsSqlServer()) await db.Entry(shipment).ReloadAsync(ct);
        if (shipperId is null || shipment.ShipperId != shipperId)
            throw new UnauthorizedAccessException("Chỉ shipper đã nhận vận đơn được cập nhật tracking.");
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 100) throw new ArgumentException("Event ID required");
        if (await db.ShippingEvents.AnyAsync(e => e.ExternalEventId == eventId, ct)) return false;
        if (shipment.Status is null || !ShippingState.TryNext(shipment.Status, nextStatus, out var status)) return false;
        if (status == "OutForDelivery" && shipment.DeliveryAttempts >= 2) return false;
        shipment.Status = status;
        var orderForUpdate = await db.OrderTables.SingleAsync(o => o.Id == shipment.OrderId, ct);
        orderForUpdate.UpdatedAt = DateTime.UtcNow;
        if (status == "OutForDelivery") shipment.DeliveryAttempts++;
        if (status == "Delivered")
        {
            shipment.DeliveredAt = Now;
            if (shipment.Direction == "Return")
            {
                var returned = await db.ReturnRequests.SingleOrDefaultAsync(r => r.OrderId == shipment.OrderId, ct);
                if (returned is not null) returned.ConfirmationDueAt = Now.AddSeconds(Math.Max(1, config?.GetValue("Returns:ReceiptSeconds", 172800) ?? 172800));
            }
        }
        if (status == "DeliveryFailed") shipment.FailureReason = note;
        db.ShippingEvents.Add(new ShippingEvent
        {
            ShippingInfoId = shippingInfoId, ExternalEventId = eventId, Status = status,
            Location = location, Note = note, OccurredAt = DateTime.UtcNow, ReceivedAt = DateTime.UtcNow
        });
        if (shipment.Direction == "Outbound" && status == "Delivered")
        {
            orderForUpdate.Status = OrderState.Next(orderForUpdate.Status!, "Deliver");
            await new CheckoutService(db).QueueEmailAsync(orderForUpdate, "Delivered", "Đơn hàng đã được giao", $"Đơn hàng #{orderForUpdate.Id} đã được giao thành công.", ct);
        }
        if (shipment.Direction == "Outbound" && status == "DeliveryFailed")
        {
            await new CheckoutService(db).QueueEmailAsync(orderForUpdate, "DeliveryFailed", "Giao hàng chưa thành công", $"Đơn hàng #{orderForUpdate.Id} chưa giao thành công. Vui lòng theo dõi các bước tiếp theo.", ct);
        }
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<ShippingInfo> ClaimAsync(int shippingInfoId, int shipperId, CancellationToken ct = default)
    {
        if (!await db.Users.AnyAsync(u => u.Id == shipperId && u.Role == "shipper", ct))
            throw new UnauthorizedAccessException("Chỉ tài khoản shipper được nhận vận đơn.");
        var query = ShipperShipmentList.Claimable(db).Where(s => s.Id == shippingInfoId);
        if (db.Database.IsRelational())
            await query.ExecuteUpdateAsync(set => set.SetProperty(s => s.ShipperId, shipperId).SetProperty(s => s.ClaimedAt, Now), ct);
        else
        {
            var pending = await query.SingleOrDefaultAsync(ct);
            if (pending is not null) { pending.ShipperId = shipperId; pending.ClaimedAt = Now; await db.SaveChangesAsync(ct); }
        }
        var shipment = await db.ShippingInfos.AsNoTracking().SingleOrDefaultAsync(s => s.Id == shippingInfoId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy vận đơn.");
        if (shipment.ShipperId != shipperId)
            throw new InvalidOperationException("Vận đơn đã được shipper khác nhận hoặc không còn chờ nhận.");
        return shipment;
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
