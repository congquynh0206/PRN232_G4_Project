using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using G4.Contracts.Checkout;
using G4.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Checkout;
public sealed class AddressChangeService(ApplicationDbContext db, ISellerFinanceService finance,
    IConfiguration? config = null, TimeProvider? clock = null) : IAddressChangeService
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    public static bool Pending(AddressChangeRequest r) => r.Status is "Pending" or "AwaitingShipper";
    private async Task<OrderTable> LockedOrderAsync(int id, CancellationToken ct)
    {
        var order = db.Database.IsSqlServer()
            ? await db.OrderTables.FromSqlInterpolated($"SELECT * FROM [OrderTable] WITH (UPDLOCK,HOLDLOCK) WHERE [id]={id}").SingleOrDefaultAsync(ct)
            : await db.OrderTables.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");
        if (db.Database.IsRelational()) await db.Entry(order).ReloadAsync(ct);
        return order;
    }
    private async Task<ShippingInfo?> ShipmentAsync(int id, CancellationToken ct)
    {
        var shipment = db.Database.IsSqlServer()
            ? await db.ShippingInfos.FromSqlInterpolated($"SELECT * FROM [ShippingInfo] WITH (UPDLOCK,HOLDLOCK) WHERE [orderId]={id} AND [Direction]='Outbound'").SingleOrDefaultAsync(ct)
            : await db.ShippingInfos.SingleOrDefaultAsync(x => x.OrderId == id && x.Direction == "Outbound", ct);
        if (shipment != null && db.Database.IsRelational()) await db.Entry(shipment).ReloadAsync(ct);
        return shipment;
    }
    private async Task EnsureEligibleAsync(OrderTable order, ShippingInfo? shipment, CancellationToken ct)
    {
        if (order.Status is not ("Paid" or "Preparing" or "Shipping") || order.AddressChangeCount != 0
            || shipment != null && (shipment.DeliveryAttempts > 0 || shipment.Status is not ("NotCreated" or "ShipmentCreationFailed" or "LabelCreated" or "PickedUp" or "InTransit")))
            throw new InvalidOperationException("Chỉ được đổi địa chỉ một lần cho đơn đã thanh toán, trước khi bắt đầu giao tới người mua.");
        if (await db.Disputes.AnyAsync(x => x.OrderId == order.Id && x.IsOpen, ct)
            || await db.ReturnRequests.AnyAsync(x => x.OrderId == order.Id && x.Status != "Rejected" && x.Status != "Closed", ct))
            throw new InvalidOperationException("Đơn đang có trả hàng hoặc tranh chấp, chưa thể đổi địa chỉ.");
    }
    private static (string State, string Country) Region(string? state, string? country, string? snapshot)
    {
        if (!string.IsNullOrWhiteSpace(state) && !string.IsNullOrWhiteSpace(country)) return (state, country);
        var parts = snapshot?.Split(',').Select(x => x.Trim()).ToArray();
        if (parts is null || parts.Length < 5 || string.IsNullOrWhiteSpace(parts[^1]) || string.IsNullOrWhiteSpace(parts[^2]))
            throw new InvalidOperationException("Đơn cũ thiếu snapshot tỉnh/quốc gia để tính lại phí ship.");
        return (parts[^2], parts[^1]);
    }
    private static string Validate(AddressChangeInput input)
    {
        var a = input.Address ?? throw new ArgumentException("Cần nhập địa chỉ mới.");
        if (new[] { a.FullName, a.Street, a.City, a.State, a.Country }.Any(string.IsNullOrWhiteSpace)
            || a.FullName.Length > 100 || a.Street.Length > 100 || a.City.Length > 50 || a.State.Length > 50 || a.Country.Length > 50
            || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 1000 || string.IsNullOrWhiteSpace(input.Key) || input.Key.Length > 100)
            throw new ArgumentException("Nhập đầy đủ địa chỉ và lý do; tên/đường tối đa 100, tỉnh/thành/quốc gia 50, lý do 1000 ký tự.");
        return string.Join(", ", new[] { a.FullName, a.Street, a.City, a.State, a.Country }.Select(x => x.Trim()));
    }
    private async Task<(AddressChangeQuoteView Quote, int Revision)> QuoteCoreAsync(OrderTable order, ShippingInfo? shipment,
        string address, string state, string country, string reason, CancellationToken ct)
    {
        var pickup = Region(order.PickupStateSnapshot, order.PickupCountrySnapshot, order.PickupAddressSnapshot);
        var delivery = Region(order.DeliveryStateSnapshot, order.DeliveryCountrySnapshot, order.AddressSnapshot);
        var weight = order.TotalWeightKg ?? throw new InvalidOperationException("Đơn thiếu khối lượng snapshot.");
        var oldCost = ShippingPricing.Calculate(weight, ShippingPricing.IsHanoiLocal(pickup.State, pickup.Country, delivery.State, delivery.Country));
        var newCost = ShippingPricing.Calculate(weight, ShippingPricing.IsHanoiLocal(pickup.State, pickup.Country, state, country));
        var payment = await db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == order.Id && x.Status == "Succeeded", ct);
        var refunds = await db.Refunds.AsNoTracking().Where(x => x.OrderId == order.Id && x.Status == "Succeeded").ToListAsync(ct);
        var remaining = (payment.Amount ?? 0) - refunds.Sum(x => x.Amount);
        var buyerShipping = Math.Max(0, order.ShippingFee - refunds.Where(x => x.Reason == "shipping-adjustment").Sum(x => x.Amount));
        var refund = Math.Max(0, Math.Min(oldCost - newCost, Math.Min(buyerShipping, remaining)));
        var pref = await db.SellerShippingPreferences.AsNoTracking().SingleOrDefaultAsync(x => x.SellerId == order.SellerId, ct);
        var cap = pref?.AddressChangeCap ?? 0m;
        var revision = pref?.Revision ?? 0;
        // A stale-confirmation fingerprint, never a source of trusted client amounts.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { order.Id, order.BuyerId, order.SellerId, order.AddressSnapshot,
            address, state, country, reason, weight, PickupState = pickup.State, PickupCountry = pickup.Country,
            DeliveryState = delivery.State, DeliveryCountry = delivery.Country, oldCost, newCost, cap, revision, refund,
            PaymentId = payment.Id, payment.Amount, remaining, buyerShipping, Shipper = shipment?.ShipperId });
        return (new(Convert.ToHexString(SHA256.HashData(bytes)), order.AddressSnapshot ?? "", address, oldCost, newCost, cap, refund,
            shipment?.ShipperId, newCost - oldCost <= cap), revision);
    }
    private async Task<(OrderTable Order, ShippingInfo? Shipment)> PrepareAsync(int id, int buyerId, string address, CancellationToken ct)
    {
        var order = await LockedOrderAsync(id, ct);
        if (order.BuyerId != buyerId) throw new UnauthorizedAccessException("Bạn không sở hữu đơn hàng này.");
        await ExpireOrderAsync(order, ct);
        var shipment = await ShipmentAsync(id, ct);
        await EnsureEligibleAsync(order, shipment, ct);
        if (await db.AddressChangeRequests.AnyAsync(x => x.OrderId == id && (x.Status == "Pending" || x.Status == "AwaitingShipper"), ct))
            throw new InvalidOperationException("Đơn đã có yêu cầu đổi địa chỉ đang chờ xác nhận.");
        if (string.Equals(order.AddressSnapshot, address, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Địa chỉ mới cần khác địa chỉ hiện tại.");
        return (order, shipment);
    }
    public async Task<AddressChangeQuoteView> QuoteAsync(int id, int buyerId, AddressChangeInput input, CancellationToken ct = default)
    {
        var address = Validate(input);
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, id, ct);
        var (order, shipment) = await PrepareAsync(id, buyerId, address, ct);
        var result = await QuoteCoreAsync(order, shipment, address, input.Address.State.Trim(), input.Address.Country.Trim(), input.Reason.Trim(), ct);
        if (tx != null) await tx.CommitAsync(ct);
        return result.Quote;
    }
    public async Task<AddressChangeView> RequestAsync(int orderId, int buyerId, AddressChangeInput input, CancellationToken ct = default)
    {
        var address = Validate(input);
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        var owned = await LockedOrderAsync(orderId, ct);
        if (owned.BuyerId != buyerId) throw new UnauthorizedAccessException("Bạn không sở hữu đơn hàng này.");
        var existing = await db.AddressChangeRequests.SingleOrDefaultAsync(x => x.RequestKey == input.Key, ct);
        if (existing != null)
        {
            if (db.Database.IsRelational()) await db.Entry(existing).ReloadAsync(ct);
            if (existing.OrderId != orderId || existing.NewAddress != address || existing.Reason != input.Reason.Trim()
                || existing.WorkflowVersion == 1 && existing.QuoteToken != input.QuoteToken)
                throw new InvalidOperationException("Khóa yêu cầu đã được dùng cho nội dung khác.");
            await ExpireOrderAsync(owned, ct);
            if (tx != null) await tx.CommitAsync(ct);
            return View(existing);
        }
        var (order, shipment) = await PrepareAsync(orderId, buyerId, address, ct);
        var (quote, revision) = await QuoteCoreAsync(order, shipment, address, input.Address.State.Trim(), input.Address.Country.Trim(), input.Reason.Trim(), ct);
        if (input.QuoteToken != quote.Token) throw new InvalidOperationException("Cần xem và xác nhận báo giá địa chỉ mới nhất trước khi gửi yêu cầu.");
        var r = new AddressChangeRequest { OrderId = orderId, RequestKey = input.Key, Reason = input.Reason.Trim(), OldAddress = quote.OldAddress,
            NewAddress = quote.NewAddress, NewState = input.Address.State.Trim(), NewCountry = input.Address.Country.Trim(), OldShippingCost = quote.OldShippingCost,
            NewShippingCost = quote.NewShippingCost, SellerCap = quote.SellerCap, BuyerRefund = quote.BuyerRefund, WorkflowVersion = 1, IsPending = true,
            QuoteToken = quote.Token, CapRevision = revision, ExpectedShipperId = quote.ShipperId, BuyerUserId = buyerId, BuyerConfirmedAt = Now, CreatedAt = Now,
            ExpiresAt = Now.AddSeconds(Math.Clamp(config?.GetValue("AddressChanges:ConfirmationSeconds", 86400) ?? 86400, 1, 604800)) };
        if (!quote.WithinCap) End(r, "Rejected", "Phí ship tăng vượt mức seller chấp nhận. Địa chỉ cũ được giữ nguyên.");
        db.AddressChangeRequests.Add(r); order.UpdatedAt = Now;
        await db.SaveChangesAsync(ct); await SaveStatusAsync(order, r, ct);
        if (tx != null) await tx.CommitAsync(ct);
        return View(r);
    }
    private void End(AddressChangeRequest r, string status, string? reason)
    { r.Status = status; r.IsPending = false; r.DecidedAt = Now; r.DecisionReason = reason; }
    private async Task<bool> RevalidateAsync(OrderTable order, ShippingInfo? shipment, AddressChangeRequest r, CancellationToken ct)
    {
        if (r.ExpiresAt != null && r.ExpiresAt <= Now) { End(r, "Expired", "Hết hạn chờ xác nhận. Địa chỉ cũ được giữ nguyên."); return false; }
        if (r.WorkflowVersion != 1) { End(r, "Rejected", "Yêu cầu cũ cần buyer xem và xác nhận báo giá mới."); return false; }
        try { await EnsureEligibleAsync(order, shipment, ct); }
        catch (InvalidOperationException ex) { End(r, "Rejected", ex.Message); return false; }
        var (quote, _) = await QuoteCoreAsync(order, shipment, r.NewAddress, r.NewState, r.NewCountry, r.Reason, ct);
        if (quote.Token != r.QuoteToken || !quote.WithinCap)
        { End(r, "Rejected", "Địa chỉ, shipper, mức chịu phí hoặc báo giá đã thay đổi. Buyer cần xem báo giá và gửi yêu cầu mới."); return false; }
        return true;
    }
    public async Task<AddressChangeView> DecideAsync(int id, int sellerId, AddressChangeDecision input, CancellationToken ct = default)
    {
        var orderId = await db.AddressChangeRequests.Where(x => x.Id == id).Select(x => (int?)x.OrderId).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu đổi địa chỉ.");
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        var order = await LockedOrderAsync(orderId, ct);
        if (order.SellerId != sellerId) throw new UnauthorizedAccessException("Bạn không sở hữu đơn hàng này.");
        var r = await db.AddressChangeRequests.SingleAsync(x => x.Id == id, ct);
        if (db.Database.IsRelational()) await db.Entry(r).ReloadAsync(ct);
        if (r.Status == "Pending")
        {
            if (!input.Approve && (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 1000)) throw new ArgumentException("Cần lý do từ chối, tối đa 1000 ký tự.");
            var shipment = await ShipmentAsync(orderId, ct);
            if (await RevalidateAsync(order, shipment, r, ct))
            {
                r.SellerUserId = sellerId;
                if (!input.Approve) End(r, "Rejected", input.Reason!.Trim());
                else { r.SellerConfirmedAt = Now; if (shipment?.ShipperId != null) r.Status = "AwaitingShipper"; else await ApplyAsync(order, shipment, r, ct); }
            }
            await SaveStatusAsync(order, r, ct);
        }
        else if ((r.Status is "AwaitingShipper" or "Approved") && !input.Approve) throw new InvalidOperationException("Yêu cầu đã có quyết định khác.");
        if (tx != null) await tx.CommitAsync(ct);
        return View(r);
    }
    public async Task<AddressChangeView> DecideShipperAsync(int shipmentId, int shipperId, AddressChangeShipperDecision input, CancellationToken ct = default)
    {
        var orderId = await db.ShippingInfos.Where(x => x.Id == shipmentId).Select(x => x.OrderId).SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException("Không tìm thấy vận đơn.");
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        var order = await LockedOrderAsync(orderId, ct);
        var shipment = await ShipmentAsync(orderId, ct);
        if (shipment == null || shipment.Id != shipmentId || shipment.ShipperId != shipperId) throw new UnauthorizedAccessException("Chỉ shipper nhận vận đơn chiều đi được quyết định địa chỉ.");
        var r = await db.AddressChangeRequests.SingleOrDefaultAsync(x => x.Id == input.RequestId && x.OrderId == orderId, ct) ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu của vận đơn.");
        if (db.Database.IsRelational()) await db.Entry(r).ReloadAsync(ct);
        if (r.Status == "AwaitingShipper")
        {
            if (!input.Approve && (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 1000)) throw new ArgumentException("Cần lý do không thể giao, tối đa 1000 ký tự.");
            if (await RevalidateAsync(order, shipment, r, ct))
            { r.ShipperUserId = shipperId; r.ShipperDecidedAt = Now; if (!input.Approve) End(r, "Rejected", input.Reason!.Trim()); else await ApplyAsync(order, shipment, r, ct); }
            await SaveStatusAsync(order, r, ct);
        }
        else if (r.Status == "Pending") throw new InvalidOperationException("Cần seller chấp thuận trước.");
        else if (r.Status == "Approved" && !input.Approve) throw new InvalidOperationException("Địa chỉ đã có hiệu lực.");
        if (tx != null) await tx.CommitAsync(ct);
        return View(r);
    }
    private async Task ApplyAsync(OrderTable order, ShippingInfo? shipment, AddressChangeRequest r, CancellationToken ct)
    {
        order.AddressSnapshot = r.NewAddress; order.DeliveryStateSnapshot = r.NewState; order.DeliveryCountrySnapshot = r.NewCountry; order.AddressChangeCount++;
        r.AppliedAt = Now; End(r, "Approved", null);
        if (shipment != null) { shipment.DeliveryAddressSnapshot = r.NewAddress; shipment.AddressChangeRequestId = r.Id; shipment.AddressChangeAcknowledgedAt = Now; }
        await db.SaveChangesAsync(ct);
        var delta = r.NewShippingCost - r.OldShippingCost;
        if (delta > 0) await finance.ApplyShippingCostAsync(order.Id, r.Id, delta, ct);
        if (r.BuyerRefund > 0)
        {
            var payment = await db.Payments.SingleAsync(x => x.OrderId == order.Id && x.Status == "Succeeded", ct);
            // Internal simulation for every original payment method, no external refund call.
            var refund = new Refund { OrderId = order.Id, PaymentId = payment.Id, Amount = r.BuyerRefund, Currency = order.Currency,
                Reason = "shipping-adjustment", IdempotencyKey = $"address-change:{r.Id}:refund", Status = "Succeeded", ProviderRefundId = $"SIM-SHIP-{r.Id}", CreatedAt = Now, CompletedAt = Now };
            db.Refunds.Add(refund); await db.SaveChangesAsync(ct); r.RefundId = refund.Id;
            await finance.ApplyRefundAsync(order.Id, refund.Id, ct);
        }
    }
    // Historical Approved requests retain their original results and acknowledgement workflow.
    public async Task AcknowledgeAsync(int shipmentId, int shipperId, CancellationToken ct = default)
    {
        var orderId = await db.ShippingInfos.Where(x => x.Id == shipmentId).Select(x => x.OrderId).SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException("Không tìm thấy vận đơn.");
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        var order = await LockedOrderAsync(orderId, ct); var shipment = await ShipmentAsync(orderId, ct);
        if (shipment == null || shipment.Id != shipmentId || shipment.ShipperId != shipperId) throw new UnauthorizedAccessException("Chỉ shipper nhận vận đơn được xác nhận.");
        var r = await db.AddressChangeRequests.SingleOrDefaultAsync(x => x.Id == shipment.AddressChangeRequestId, ct);
        if (r == null || r.Status != "Approved") throw new InvalidOperationException("Dùng bước chấp thuận khả năng giao trước khi áp dụng địa chỉ.");
        if (r.WorkflowVersion == 0 && shipment.AddressChangeAcknowledgedAt == null) { shipment.AddressChangeAcknowledgedAt = Now; order.UpdatedAt = Now; await db.SaveChangesAsync(ct); }
        if (tx != null) await tx.CommitAsync(ct);
    }
    public async Task MaintainAsync(CancellationToken ct = default)
    {
        var ids = await db.AddressChangeRequests.AsNoTracking().Where(x => (x.Status == "Pending" || x.Status == "AwaitingShipper") && x.ExpiresAt <= Now)
            .OrderBy(x => x.ExpiresAt).Select(x => x.OrderId).Take(50).ToListAsync(ct);
        foreach (var id in ids) { await using var tx = await PromotionLifecycle.LockOrderAsync(db, id, ct); await ExpireOrderAsync(await LockedOrderAsync(id, ct), ct); if (tx != null) await tx.CommitAsync(ct); }
    }
    private async Task ExpireOrderAsync(OrderTable order, CancellationToken ct)
    {
        var rows = await db.AddressChangeRequests.Where(x => x.OrderId == order.Id && (x.Status == "Pending" || x.Status == "AwaitingShipper")).ToListAsync(ct);
        foreach (var r in rows) { if (db.Database.IsRelational()) await db.Entry(r).ReloadAsync(ct); if (Pending(r) && r.ExpiresAt <= Now) { End(r, "Expired", "Hết hạn chờ xác nhận. Địa chỉ cũ được giữ nguyên."); await SaveStatusAsync(order, r, ct); } }
    }
    private async Task SaveStatusAsync(OrderTable order, AddressChangeRequest r, CancellationToken ct)
    { order.UpdatedAt = Now; await EnqueueStatusAsync(db, order, r, config, ct); await db.SaveChangesAsync(ct); }
    internal static async Task EnqueueStatusAsync(ApplicationDbContext db, OrderTable order, AddressChangeRequest r, IConfiguration? config, CancellationToken ct)
    {
        var ids = new[] { order.BuyerId, order.SellerId, r.Status == "AwaitingShipper" ? r.ExpectedShipperId : null };
        var users = await db.Users.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        var title = r.Status switch { "Approved" => "Địa chỉ mới đã có hiệu lực", "AwaitingShipper" => "Chờ shipper chấp thuận khả năng giao", "Rejected" => "Yêu cầu đổi địa chỉ bị từ chối", "Expired" => "Yêu cầu đổi địa chỉ hết hạn", _ => "Yêu cầu đổi địa chỉ cần xử lý" };
        var body = $"Đơn #{order.Id}\n{title}\nĐịa chỉ cũ: {r.OldAddress}\nĐịa chỉ yêu cầu: {r.NewAddress}\nLý do: {r.Reason}\n{r.DecisionReason}\nHạn chờ (UTC): {r.ExpiresAt:O}\n{(r.Status == "Approved" ? "Hoàn ship giả lập đã áp dụng" : "Hoàn ship giả lập dự kiến nếu đủ xác nhận")}: {r.BuyerRefund:F2} {order.Currency}";
        foreach (var user in users)
        {
            var type = r.WorkflowVersion == 1 ? $"AddressChange{r.Status}" : $"AddressChange:{r.Id}:{r.Status}:{user.Id}";
            var key = r.WorkflowVersion == 1 ? $"address-change:{r.Id}:{r.Status}:user:{user.Id}" : null;
            if (db.NotificationOutbox.Local.Any(x => key != null ? x.EventKey == key : x.OrderId == order.Id && x.EventType == type)
                || await db.NotificationOutbox.AnyAsync(x => key != null ? x.EventKey == key : x.OrderId == order.Id && x.EventType == type, ct)) continue;
            var valid = G4.Infrastructure.Notifications.NotificationService.IsAddress(user.Email);
            db.NotificationOutbox.Add(new NotificationOutbox { OrderId = order.Id, RecipientUserId = user.Id, EventType = type, EventKey = key, Recipient = user.Email ?? "", From = config?["Mail:From"] ?? "g4@example.test", Subject = $"{title} — Đơn #{order.Id}", Body = body, HtmlBody = $"<div style='white-space:pre-wrap'>{WebUtility.HtmlEncode(body)}</div>", Status = valid ? "Pending" : "Failed", LastErrorCode = valid ? null : "RecipientInvalid", CreatedAt = DateTime.UtcNow });
        }
    }
    private static DateTime? Utc(DateTime? v) => v == null ? null : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc);
    public static AddressChangeView View(AddressChangeRequest r) => new(r.Id, r.OrderId, r.Reason, r.OldAddress, r.NewAddress, r.OldShippingCost, r.NewShippingCost, r.SellerCap, r.BuyerRefund, r.Status, r.DecisionReason, Utc(r.CreatedAt)!.Value, Utc(r.DecidedAt), r.WorkflowVersion, Utc(r.ExpiresAt), Utc(r.BuyerConfirmedAt), Utc(r.SellerConfirmedAt), r.ExpectedShipperId, Utc(r.ShipperDecidedAt), Utc(r.AppliedAt), r.BuyerUserId, r.SellerUserId, r.ShipperUserId);
}
