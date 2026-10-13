using System.Net;
using G4.Contracts.Checkout;
using G4.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Checkout;

public sealed class AddressChangeService(ApplicationDbContext db, ISellerFinanceService finance, IConfiguration? config = null) : IAddressChangeService
{
    private async Task<OrderTable> LockedOrderAsync(int orderId, CancellationToken ct)
    {
        var order = db.Database.IsSqlServer()
            ? await db.OrderTables.FromSqlInterpolated($"SELECT * FROM [OrderTable] WITH (UPDLOCK,HOLDLOCK) WHERE [id]={orderId}").SingleOrDefaultAsync(ct)
            : await db.OrderTables.SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");
        if (db.Database.IsRelational()) await db.Entry(order).ReloadAsync(ct);
        return order;
    }

    private async Task<ShippingInfo?> ShipmentAsync(int orderId, CancellationToken ct)
    {
        var shipment = db.Database.IsSqlServer()
            ? await db.ShippingInfos.FromSqlInterpolated($"SELECT * FROM [ShippingInfo] WITH (UPDLOCK,HOLDLOCK) WHERE [orderId]={orderId} AND [Direction]='Outbound'").SingleOrDefaultAsync(ct)
            : await db.ShippingInfos.SingleOrDefaultAsync(x => x.OrderId == orderId && x.Direction == "Outbound", ct);
        if (shipment != null && db.Database.IsRelational()) await db.Entry(shipment).ReloadAsync(ct);
        return shipment;
    }

    private async Task EnsureEligibleAsync(OrderTable order, ShippingInfo? shipment, CancellationToken ct)
    {
        if (order.Status is not ("Paid" or "Preparing" or "Shipping") || order.AddressChangeCount != 0 ||
            shipment != null && (shipment.DeliveryAttempts > 0 || shipment.Status is not ("NotCreated" or "ShipmentCreationFailed" or "LabelCreated" or "PickedUp" or "InTransit")))
            throw new InvalidOperationException("Chỉ được đổi địa chỉ một lần cho đơn đã thanh toán, trước khi bắt đầu giao tới người mua.");
        if (await db.Disputes.AnyAsync(x => x.OrderId == order.Id && x.IsOpen, ct) ||
            await db.ReturnRequests.AnyAsync(x => x.OrderId == order.Id && x.Status != "Rejected" && x.Status != "Closed", ct))
            throw new InvalidOperationException("Đơn đang có trả hàng hoặc tranh chấp, chưa thể đổi địa chỉ.");
    }

    private static (string State, string Country) Region(string? state, string? country, string? snapshot)
    {
        if (!string.IsNullOrWhiteSpace(state) && !string.IsNullOrWhiteSpace(country)) return (state, country);
        // Legacy orders retain their original address text; never read the buyer/seller's editable profile.
        var parts = snapshot?.Split(',').Select(x => x.Trim()).ToArray();
        if (parts is null || parts.Length < 5 || string.IsNullOrWhiteSpace(parts[^1]) || string.IsNullOrWhiteSpace(parts[^2]))
            throw new InvalidOperationException("Đơn cũ thiếu snapshot tỉnh/quốc gia để tính lại phí ship.");
        return (parts[^2], parts[^1]);
    }

    private async Task<decimal> CapAsync(int? seller, CancellationToken ct) =>
        await db.SellerShippingPreferences.Where(x => x.SellerId == seller).Select(x => (decimal?)x.AddressChangeCap).SingleOrDefaultAsync(ct) ?? 0m;

    public async Task<AddressChangeView> RequestAsync(int orderId, int buyerId, AddressChangeInput input, CancellationToken ct = default)
    {
        var a = input.Address ?? throw new ArgumentException("Cần nhập địa chỉ mới.");
        if (new[] { a.FullName, a.Street, a.City, a.State, a.Country }.Any(string.IsNullOrWhiteSpace) ||
            a.FullName.Length > 100 || a.Street.Length > 100 || a.City.Length > 50 || a.State.Length > 50 || a.Country.Length > 50 ||
            string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 1000 || string.IsNullOrWhiteSpace(input.Key) || input.Key.Length > 100)
            throw new ArgumentException("Nhập đầy đủ địa chỉ và lý do; tên/đường tối đa 100, tỉnh/thành/quốc gia 50, lý do 1000 ký tự.");
        var address = string.Join(", ", new[] { a.FullName, a.Street, a.City, a.State, a.Country }.Select(x => x.Trim()));
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        var order = await LockedOrderAsync(orderId, ct);
        if (order.BuyerId != buyerId) throw new UnauthorizedAccessException("Bạn không sở hữu đơn hàng này.");
        var existing = await db.AddressChangeRequests.SingleOrDefaultAsync(x => x.RequestKey == input.Key, ct);
        if (existing != null)
        {
            if (existing.OrderId != orderId || existing.NewAddress != address || existing.Reason != input.Reason.Trim())
                throw new InvalidOperationException("Khóa yêu cầu đã được dùng cho nội dung khác.");
            if (tx != null) await tx.CommitAsync(ct);
            return View(existing);
        }
        var shipment = await ShipmentAsync(orderId, ct);
        await EnsureEligibleAsync(order, shipment, ct);
        if (await db.AddressChangeRequests.AnyAsync(x => x.OrderId == orderId && x.Status == "Pending", ct))
            throw new InvalidOperationException("Đơn đã có yêu cầu đổi địa chỉ đang chờ seller duyệt.");
        if (string.Equals(order.AddressSnapshot, address, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Địa chỉ mới cần khác địa chỉ hiện tại.");
        var pickup = Region(order.PickupStateSnapshot, order.PickupCountrySnapshot, order.PickupAddressSnapshot);
        var delivery = Region(order.DeliveryStateSnapshot, order.DeliveryCountrySnapshot, order.AddressSnapshot);
        var weight = order.TotalWeightKg ?? throw new InvalidOperationException("Đơn thiếu khối lượng snapshot.");
        var oldCost = ShippingPricing.Calculate(weight, ShippingPricing.IsHanoiLocal(pickup.State, pickup.Country, delivery.State, delivery.Country));
        var newCost = ShippingPricing.Calculate(weight, ShippingPricing.IsHanoiLocal(pickup.State, pickup.Country, a.State, a.Country));
        var payment = await db.Payments.SingleAsync(x => x.OrderId == orderId && x.Status == "Succeeded", ct);
        var remaining = (payment.Amount ?? 0) - await db.Refunds.Where(x => x.OrderId == orderId && x.Status == "Succeeded").SumAsync(x => x.Amount, ct);
        var cap = await CapAsync(order.SellerId, ct);
        var request = new AddressChangeRequest
        {
            OrderId = orderId, RequestKey = input.Key, Reason = input.Reason.Trim(), OldAddress = order.AddressSnapshot ?? "",
            NewAddress = address, NewState = a.State.Trim(), NewCountry = a.Country.Trim(), OldShippingCost = oldCost, NewShippingCost = newCost,
            SellerCap = cap, BuyerRefund = Math.Max(0, Math.Min(Math.Max(0, oldCost - newCost), Math.Min(order.ShippingFee, remaining))), CreatedAt = DateTime.UtcNow
        };
        if (newCost - oldCost > cap)
        {
            request.Status = "Rejected"; request.DecidedAt = DateTime.UtcNow;
            request.DecisionReason = "Phí ship tăng vượt mức seller chấp nhận. Địa chỉ cũ được giữ nguyên.";
        }
        db.AddressChangeRequests.Add(request); order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await EnqueueStatusAsync(db, order, request, config, ct);
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return View(request);
    }

    public async Task<AddressChangeView> DecideAsync(int id, int sellerId, AddressChangeDecision input, CancellationToken ct = default)
    {
        var orderId = await db.AddressChangeRequests.Where(x => x.Id == id).Select(x => (int?)x.OrderId).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu đổi địa chỉ.");
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        var order = await LockedOrderAsync(orderId, ct);
        if (order.SellerId != sellerId) throw new UnauthorizedAccessException("Bạn không sở hữu đơn hàng này.");
        var request = await db.AddressChangeRequests.SingleAsync(x => x.Id == id, ct);
        if (db.Database.IsRelational()) await db.Entry(request).ReloadAsync(ct);
        if (request.Status != "Pending")
        {
            if ((request.Status == "Approved") != input.Approve) throw new InvalidOperationException("Yêu cầu đã có quyết định khác.");
            if (tx != null) await tx.CommitAsync(ct);
            return View(request);
        }
        if (!input.Approve && (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 1000))
            throw new ArgumentException("Cần lý do từ chối, tối đa 1000 ký tự.");
        var shipment = await ShipmentAsync(orderId, ct);
        if (input.Approve) await EnsureEligibleAsync(order, shipment, ct);
        request.SellerCap = await CapAsync(order.SellerId, ct);
        var delta = request.NewShippingCost - request.OldShippingCost;
        var approve = input.Approve && delta <= request.SellerCap;
        request.Status = approve ? "Approved" : "Rejected";
        request.DecidedAt = DateTime.UtcNow;
        request.DecisionReason = approve ? null : input.Approve ? "Phí ship tăng vượt mức seller hiện chấp nhận." : input.Reason!.Trim();
        if (approve)
        {
            if (order.AddressSnapshot != request.OldAddress) throw new InvalidOperationException("Địa chỉ đơn đã thay đổi. Hãy tải lại yêu cầu.");
            order.AddressSnapshot = request.NewAddress; order.DeliveryStateSnapshot = request.NewState;
            order.DeliveryCountrySnapshot = request.NewCountry; order.AddressChangeCount++;
            if (shipment != null)
            {
                shipment.DeliveryAddressSnapshot = request.NewAddress;
                shipment.AddressChangeRequestId = request.Id; shipment.AddressChangeAcknowledgedAt = null;
            }
            await db.SaveChangesAsync(ct);
            if (delta > 0) await finance.ApplyShippingCostAsync(orderId, request.Id, delta, ct);
            if (request.BuyerRefund > 0)
            {
                var payment = await db.Payments.SingleAsync(x => x.OrderId == orderId && x.Status == "Succeeded", ct);
                var remaining = (payment.Amount ?? 0) - await db.Refunds.Where(x => x.OrderId == orderId && x.Status == "Succeeded").SumAsync(x => x.Amount, ct);
                if (request.BuyerRefund > remaining) throw new InvalidOperationException("Không còn đủ tiền đã trả để hoàn chênh lệch ship.");
                // Shipping difference is an internal simulation for every original payment method; no external capture/refund is called.
                var refund = new Refund { OrderId = orderId, PaymentId = payment.Id, Amount = request.BuyerRefund, Currency = order.Currency,
                    Reason = "shipping-adjustment", IdempotencyKey = $"address-change:{request.Id}:refund", Status = "Succeeded",
                    ProviderRefundId = $"SIM-SHIP-{request.Id}", CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow };
                db.Refunds.Add(refund); await db.SaveChangesAsync(ct); request.RefundId = refund.Id;
                await finance.ApplyRefundAsync(orderId, refund.Id, ct);
            }
        }
        order.UpdatedAt = DateTime.UtcNow;
        await EnqueueStatusAsync(db, order, request, config, ct);
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return View(request);
    }

    public async Task AcknowledgeAsync(int shipmentId, int shipperId, CancellationToken ct = default)
    {
        var orderId = await db.ShippingInfos.Where(x => x.Id == shipmentId).Select(x => x.OrderId).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Không tìm thấy vận đơn.");
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        var order = await LockedOrderAsync(orderId, ct);
        var shipment = await ShipmentAsync(orderId, ct);
        if (shipment == null || shipment.Id != shipmentId || shipment.ShipperId != shipperId)
            throw new UnauthorizedAccessException("Chỉ shipper nhận vận đơn chiều đi được xác nhận địa chỉ.");
        if (shipment.AddressChangeRequestId == null) throw new InvalidOperationException("Vận đơn không có địa chỉ mới cần xác nhận.");
        var request = await db.AddressChangeRequests.SingleAsync(x => x.Id == shipment.AddressChangeRequestId, ct);
        if (request.Status != "Approved") throw new InvalidOperationException("Seller chưa duyệt địa chỉ mới.");
        if (shipment.AddressChangeAcknowledgedAt == null)
        {
            shipment.AddressChangeAcknowledgedAt = DateTime.UtcNow; order.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        if (tx != null) await tx.CommitAsync(ct);
    }

    internal static async Task EnqueueStatusAsync(ApplicationDbContext db, OrderTable order, AddressChangeRequest request, IConfiguration? config, CancellationToken ct)
    {
        var users = await db.Users.Where(x => x.Id == order.BuyerId || x.Id == order.SellerId).ToListAsync(ct);
        var title = request.Status switch { "Approved" => "Đã duyệt đổi địa chỉ", "Rejected" => "Yêu cầu đổi địa chỉ bị từ chối", _ => "Yêu cầu đổi địa chỉ cần xử lý" };
        var body = $"Đơn #{order.Id}\n{title}\nĐịa chỉ cũ: {request.OldAddress}\nĐịa chỉ yêu cầu: {request.NewAddress}\nLý do: {request.Reason}\n{request.DecisionReason}\nHoàn chênh lệch ship giả lập khi duyệt: {request.BuyerRefund:F2} {order.Currency}";
        foreach (var user in users)
        {
            var type = $"AddressChange:{request.Id}:{request.Status}:{user.Id}";
            if (db.NotificationOutbox.Local.Any(x => x.OrderId == order.Id && x.EventType == type) || await db.NotificationOutbox.AnyAsync(x => x.OrderId == order.Id && x.EventType == type, ct)) continue;
            var valid = G4.Infrastructure.Notifications.NotificationService.IsAddress(user.Email);
            db.NotificationOutbox.Add(new NotificationOutbox { OrderId = order.Id, EventType = type, Recipient = user.Email ?? "", From = config?["Mail:From"] ?? "g4@example.test",
                Subject = $"{title} — Đơn #{order.Id}", Body = body, HtmlBody = $"<div style='white-space:pre-wrap'>{WebUtility.HtmlEncode(body)}</div>",
                Status = valid ? "Pending" : "Failed", LastErrorCode = valid ? null : "RecipientInvalid", CreatedAt = DateTime.UtcNow });
        }
    }

    public static AddressChangeView View(AddressChangeRequest r) => new(r.Id, r.OrderId, r.Reason, r.OldAddress, r.NewAddress,
        r.OldShippingCost, r.NewShippingCost, r.SellerCap, r.BuyerRefund, r.Status, r.DecisionReason, r.CreatedAt, r.DecidedAt);
}
