using G4.Domain.Entities;
using G4.Infrastructure.Diagnostics;
using G4.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Shipping;

public sealed class ShipmentService(
    ApplicationDbContext db,
    ICarrierGateway carrier,
    IConfiguration? config = null,
    TimeProvider? clock = null
) : IShipmentService
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async Task<ShippingInfo> CreateOutboundAsync(int orderId, CancellationToken ct = default)
    {
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        var existing = await db.ShippingInfos.SingleOrDefaultAsync(
            s => s.OrderId == orderId && s.Direction == "Outbound",
            ct
        );
        if (existing is not null && existing.TrackingNumber is not null)
            return existing;
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        if (db.Database.IsRelational())
            await db.Entry(order).ReloadAsync(ct);
        if (order.Status is not ("Paid" or "Preparing"))
            throw new InvalidOperationException("Order is not ready to ship");
        var key = $"ship-{orderId}-outbound";
        var shipment =
            existing
            ?? new ShippingInfo
            {
                OrderId = orderId,
                Direction = "Outbound",
                Status = "NotCreated",
                IdempotencyKey = key,
                CreatedAt = DateTime.UtcNow,
                Carrier = "G4 Carrier Simulator",
                PickupAddressSnapshot = order.PickupAddressSnapshot,
                DeliveryAddressSnapshot = order.AddressSnapshot,
            };
        if (existing is null)
            db.ShippingInfos.Add(shipment);
        shipment.DeliveryAddressSnapshot = order.AddressSnapshot;
        if (order.AddressChangeCount > 0)
        {
            shipment.AddressChangeRequestId = await db
                .AddressChangeRequests.Where(x => x.OrderId == orderId && x.Status == "Approved")
                .Select(x => (int?)x.Id)
                .SingleOrDefaultAsync(ct);
            if (
                await db.AddressChangeRequests.AnyAsync(
                    x => x.Id == shipment.AddressChangeRequestId && x.WorkflowVersion == 1,
                    ct
                )
            )
                shipment.AddressChangeAcknowledgedAt = Now;
        }
        await db.SaveChangesAsync(ct);
        try
        {
            shipment.TrackingNumber = await RetryLabelAsync(orderId, "Outbound", key, ct);
            shipment.Status = ShippingState.Next("NotCreated", "LabelCreated");
            order.Status = "Shipping";
            order.UpdatedAt = DateTime.UtcNow;
            db.ShippingEvents.Add(
                new ShippingEvent
                {
                    ShippingInfoId = shipment.Id,
                    ExternalEventId = $"{key}-label",
                    Status = "LabelCreated",
                    OccurredAt = Now,
                    ReceivedAt = Now,
                    ActorRole = "system",
                    Note = "Đã tạo vận đơn giao hàng",
                }
            );
        }
        catch (Exception ex)
            when (ex is HttpRequestException
                || ex is TaskCanceledException && !ct.IsCancellationRequested
            )
        {
            shipment.Status = "ShipmentCreationFailed";
            order.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return shipment;
    }

    public async Task<ShippingInfo> CreateReturnAsync(int orderId, CancellationToken ct = default)
    {
        var existing = await db.ShippingInfos.SingleOrDefaultAsync(
            s => s.OrderId == orderId && s.Direction == "Return",
            ct
        );
        if (existing is not null && existing.TrackingNumber is not null)
            return existing;
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        var key = $"ship-{orderId}-return";
        var shipment =
            existing
            ?? new ShippingInfo
            {
                OrderId = orderId,
                Direction = "Return",
                Status = "NotCreated",
                IdempotencyKey = key,
                CreatedAt = DateTime.UtcNow,
                Carrier = "G4 Carrier Simulator",
                PickupAddressSnapshot = order.AddressSnapshot,
                DeliveryAddressSnapshot = order.PickupAddressSnapshot,
            };
        if (existing is null)
            db.ShippingInfos.Add(shipment);
        await db.SaveChangesAsync(ct);
        try
        {
            shipment.TrackingNumber = await RetryLabelAsync(orderId, "Return", key, ct);
            shipment.Status = "LabelCreated";
            order.UpdatedAt = DateTime.UtcNow;
            db.ShippingEvents.Add(
                new ShippingEvent
                {
                    ShippingInfoId = shipment.Id,
                    ExternalEventId = $"{key}-label",
                    Status = "LabelCreated",
                    OccurredAt = Now,
                    ReceivedAt = Now,
                    ActorRole = "system",
                    Note = "Đã tạo vận đơn trả hàng",
                }
            );
        }
        catch (Exception ex)
            when (ex is HttpRequestException
                || ex is TaskCanceledException && !ct.IsCancellationRequested
            )
        {
            shipment.Status = "ShipmentCreationFailed";
            order.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return shipment;
    }

    public async Task<bool> RecordEventAsync(
        int shippingInfoId,
        string eventId,
        string nextStatus,
        string? location = null,
        string? note = null,
        CancellationToken ct = default,
        int? shipperId = null,
        DateTimeOffset? reattemptAt = null
    )
    {
        var orderId =
            await db
                .ShippingInfos.AsNoTracking()
                .Where(x => x.Id == shippingInfoId)
                .Select(x => x.OrderId)
                .SingleAsync(ct)
            ?? throw new KeyNotFoundException("Không tìm thấy đơn của vận đơn.");
        await using var transaction = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        var shipment = db.Database.IsSqlServer()
            ? await db
                .ShippingInfos.FromSqlInterpolated(
                    $"SELECT * FROM [ShippingInfo] WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {shippingInfoId}"
                )
                .SingleAsync(ct)
            : await db.ShippingInfos.SingleAsync(s => s.Id == shippingInfoId, ct);
        if (db.Database.IsSqlServer())
            await db.Entry(shipment).ReloadAsync(ct);
        if (shipperId is null || shipment.ShipperId != shipperId)
            throw new UnauthorizedAccessException(
                "Chỉ shipper đã nhận vận đơn được cập nhật tracking."
            );
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 100)
            throw new ArgumentException("Event ID required");
        location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        reattemptAt = reattemptAt?.ToUniversalTime();
        var existing = await db
            .ShippingEvents.AsNoTracking()
            .SingleOrDefaultAsync(e => e.ExternalEventId == eventId, ct);
        if (existing != null)
        {
            if (
                existing.ShippingInfoId != shippingInfoId
                || existing.Status != nextStatus
                || existing.Location != location
                || existing.Note != note
                || existing.ReattemptAt != reattemptAt
                || existing.ActorUserId != shipperId
            )
                throw new InvalidOperationException("Event ID đã được dùng cho một cập nhật khác.");
            if (transaction != null)
                await transaction.CommitAsync(ct);
            return false;
        }
        if (location?.Length > 100 || note?.Length > 1000)
            throw new ArgumentException(
                "Địa điểm tối đa 100 ký tự, ghi chú/lý do tối đa 1000 ký tự."
            );
        if (nextStatus == "ReattemptScheduled")
        {
            if (reattemptAt == null || reattemptAt <= new DateTimeOffset(Now) || note == null)
                throw new ArgumentException(
                    "Cần thời gian giao lại dự kiến trong tương lai và lý do lên lịch."
                );
        }
        else if (reattemptAt != null)
            throw new ArgumentException("Chỉ nhập lịch dự kiến cho bước lên lịch giao lại.");
        if (
            shipment.Status is null
            || !ShippingState.TryNext(shipment.Status, nextStatus, out var status)
        )
            return false;
        if (shipment.AddressChangeRequestId != null && shipment.AddressChangeAcknowledgedAt == null)
            throw new InvalidOperationException(
                "Cần xác nhận địa chỉ mới đã được seller duyệt trước khi cập nhật vận chuyển."
            );
        if (status is "OutForDelivery" or "ReattemptScheduled" && shipment.DeliveryAttempts >= 2)
            return false;
        if (status == "ReattemptScheduled")
        {
            shipment.ReattemptAt = reattemptAt;
            shipment.ReattemptReason = note;
        }
        var orderForUpdate = await db.OrderTables.SingleAsync(o => o.Id == shipment.OrderId, ct);
        if (db.Database.IsRelational())
            await db.Entry(orderForUpdate).ReloadAsync(ct);
        if (status == "OutForDelivery" && shipment.Direction == "Outbound")
        {
            var pending = await db
                .AddressChangeRequests.Where(x =>
                    x.OrderId == orderId && (x.Status == "Pending" || x.Status == "AwaitingShipper")
                )
                .ToListAsync(ct);
            if (pending.Any(x => x.ExpiresAt == null || x.ExpiresAt > Now))
                throw new InvalidOperationException(
                    "Đang chờ xác nhận đổi địa chỉ. Chưa thể bắt đầu giao tới buyer."
                );
            foreach (var change in pending)
            {
                change.Status = "Expired";
                change.IsPending = false;
                change.DecidedAt = Now;
                change.DecisionReason = "Hết hạn chờ xác nhận. Giữ địa chỉ cũ khi bắt đầu giao.";
                await AddressChangeService.EnqueueStatusAsync(
                    db,
                    orderForUpdate,
                    change,
                    config,
                    ct
                );
            }
        }

        shipment.Status = status;
        orderForUpdate.UpdatedAt = Now;
        if (status == "OutForDelivery")
            shipment.DeliveryAttempts++;
        if (status == "Delivered")
        {
            shipment.DeliveredAt = Now;
            if (shipment.Direction == "Return")
            {
                var returned = await db.ReturnRequests.SingleOrDefaultAsync(
                    r => r.OrderId == shipment.OrderId,
                    ct
                );
                if (returned is not null)
                    returned.ConfirmationDueAt = Now.AddSeconds(
                        Math.Max(1, config?.GetValue("Returns:ReceiptSeconds", 172800) ?? 172800)
                    );
            }
        }

        if (status == "DeliveryFailed")
            shipment.FailureReason = note;
        db.ShippingEvents.Add(
            new ShippingEvent
            {
                ShippingInfoId = shippingInfoId,
                ExternalEventId = eventId,
                Status = status,
                Location = location,
                Note = note,
                OccurredAt = Now,
                ReceivedAt = Now,
                ActorUserId = shipperId,
                ActorRole = "shipper",
                ReattemptAt = reattemptAt,
            }
        );
        if (status == "ReattemptScheduled")
            await new G4.Infrastructure.Notifications.BusinessNotificationService(
                db,
                config,
                clock
            ).ShipmentReattemptAsync(orderForUpdate, shipment, eventId, ct);
        if (shipment.Direction == "Outbound" && status == "Delivered")
        {
            G4.Infrastructure.Returns.ReturnWindowPolicy.SnapshotDelivery(
                orderForUpdate,
                shipment.DeliveredAt!.Value,
                config
            );
            orderForUpdate.Status = OrderState.Next(orderForUpdate.Status!, "Deliver");
            await new CheckoutService(db, config: config).QueueEmailAsync(
                orderForUpdate,
                "Delivered",
                "Đơn hàng đã được giao",
                $"Đơn hàng #{orderForUpdate.Id} đã được giao thành công.",
                ct
            );
        }

        if (shipment.Direction == "Outbound" && status == "DeliveryFailed")
        {
            await new CheckoutService(db, config: config).QueueEmailAsync(
                orderForUpdate,
                "DeliveryFailed",
                "Giao hàng chưa thành công",
                $"Đơn hàng #{orderForUpdate.Id} chưa giao thành công. Vui lòng theo dõi các bước tiếp theo.",
                ct
            );
        }

        await db.SaveChangesAsync(ct);
        if (shipment.Direction == "Outbound" && status == "Delivered")
            await new G4.Infrastructure.Finance.SellerFinanceService(
                db,
                config ?? new ConfigurationBuilder().Build(),
                clock
            ).StartDeliveryHoldAsync(orderId, ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<ShippingInfo> ClaimAsync(
        int shippingInfoId,
        int shipperId,
        CancellationToken ct = default
    )
    {
        var orderId =
            await db
                .ShippingInfos.AsNoTracking()
                .Where(x => x.Id == shippingInfoId)
                .Select(x => x.OrderId)
                .SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Không tìm thấy vận đơn.");
        await using var transaction = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        if (!await db.Users.AnyAsync(u => u.Id == shipperId && u.Role == "shipper", ct))
            throw new UnauthorizedAccessException("Chỉ tài khoản shipper được nhận vận đơn.");
        var query = ShipperShipmentList.Claimable(db).Where(s => s.Id == shippingInfoId);
        if (db.Database.IsRelational())
        {
            var changed = await query.ExecuteUpdateAsync(
                set =>
                    set.SetProperty(s => s.ShipperId, shipperId).SetProperty(s => s.ClaimedAt, Now),
                ct
            );
            if (changed > 0)
            {
                db.CommerceChanges.Add(
                    new CommerceChange
                    {
                        Topic = "shipments",
                        ShippingInfoId = shippingInfoId,
                        ShipperId = shipperId,
                        CreatedAt = Now,
                        NotificationTitle = "Đã nhận vận đơn",
                        NotificationMessage = $"Shipper đã nhận vận đơn #{shippingInfoId}.",
                    }
                );
                await db.SaveChangesAsync(ct);
            }
        }
        else
        {
            var pending = await query.SingleOrDefaultAsync(ct);
            if (pending is not null)
            {
                pending.ShipperId = shipperId;
                pending.ClaimedAt = Now;
                await db.SaveChangesAsync(ct);
            }
        }

        var shipment =
            await db
                .ShippingInfos.AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == shippingInfoId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy vận đơn.");
        if (shipment.ShipperId != shipperId)
            throw new InvalidOperationException(
                "Vận đơn đã được shipper khác nhận hoặc không còn chờ nhận."
            );
        if (transaction != null)
            await transaction.CommitAsync(ct);
        return shipment;
    }

    private async Task<string> RetryLabelAsync(
        int orderId,
        string direction,
        string key,
        CancellationToken ct
    )
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var context = IntegrationContext.ForOrder(orderId, attempt);
            try
            {
                return await carrier.CreateLabelAsync(orderId, direction, key, ct);
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await Task.Delay(50 * (1 << (attempt - 1)), ct);
            }
        }

        throw new HttpRequestException("Carrier unavailable after three attempts");
    }
}
