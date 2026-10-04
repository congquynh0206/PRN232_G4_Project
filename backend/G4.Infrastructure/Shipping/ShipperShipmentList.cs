using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Shipping;

public static class ShipperShipmentList
{
    public static IQueryable<ShippingInfo> Claimable(ApplicationDbContext db) => db.ShippingInfos.Where(s =>
        s.ShipperId == null && s.TrackingNumber != null &&
        (s.Status == "LabelCreated" || s.Status == "PickedUp" || s.Status == "InTransit" || s.Status == "OutForDelivery" ||
         s.Status == "DeliveryFailed" || s.Status == "ReturningToSender") &&
        db.OrderTables.Any(o => o.Id == s.OrderId && o.Status != "Closed" && o.Status != "Cancelled" && o.Status != "Expired"));

    public static async Task<ShipperShipmentPage> ReadAsync(ApplicationDbContext db, int shipperId, string filter,
        int page, int pageSize, CancellationToken ct = default, string? search = null, string status = "all", string direction = "all")
    {
        var pending = Claimable(db).AsNoTracking();
        var mine = db.ShippingInfos.AsNoTracking().Where(s => s.ShipperId == shipperId);
        var pendingCount = await pending.CountAsync(ct);
        var mineCount = await mine.CountAsync(ct);
        var query = filter == "pending" ? pending : mine;
        var count = filter == "pending" ? pendingCount : mineCount;
        search = search?.Trim();
        var filtered = !string.IsNullOrEmpty(search) || status != "all" || direction != "all";
        if (!string.IsNullOrEmpty(search))
        {
            var hasOrderId = int.TryParse(search.TrimStart('#'), out var orderId);
            query = query.Where(s => (s.TrackingNumber != null && s.TrackingNumber.Contains(search)) ||
                (hasOrderId && s.OrderId == orderId));
        }
        if (status != "all") query = query.Where(s => s.Status == status);
        if (direction != "all") query = query.Where(s => s.Direction == direction);
        if (filtered) count = await query.CountAsync(ct);
        pageSize = Math.Clamp(pageSize, 1, 50);
        page = Math.Clamp(page, 1, Math.Max(1, (count + pageSize - 1) / pageSize));
        var items = await query.OrderByDescending(s => s.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Join(db.OrderTables, s => s.OrderId, o => o.Id, (s, o) => new ShipperShipmentCard(s.Id, o.Id,
                s.Direction, s.TrackingNumber, s.Status, s.DeliveryAttempts, s.ShipperId, s.ClaimedAt, s.CreatedAt,
                s.PickupAddressSnapshot ?? (s.Direction == "Return" ? o.AddressSnapshot : o.PickupAddressSnapshot),
                s.DeliveryAddressSnapshot ?? (s.Direction == "Return" ? o.PickupAddressSnapshot : o.AddressSnapshot), o.TotalWeightKg))
            .ToListAsync(ct);
        return new(page, pageSize, count, pendingCount, mineCount, items);
    }
}
