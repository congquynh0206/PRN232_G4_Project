using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Returns;

internal static class ReturnQuoteBuilder
{
    public static async Task<ReturnQuote> BuildAsync(ApplicationDbContext db, int orderId, string reasonCode,
        DateTime now, CancellationToken ct)
    {
        var label = ReturnPricing.ReasonLabel(reasonCode);
        var order = await db.OrderTables.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy đơn hàng.");
        var delivered = await db.ShippingInfos.AsNoTracking().Where(x => x.OrderId == orderId && x.Direction == "Outbound")
            .Select(x => x.DeliveredAt).SingleOrDefaultAsync(ct);
        if (order.Status != "Delivered" || delivered == null || delivered < now.AddDays(-7))
            throw new InvalidOperationException("Đơn chưa giao thành công hoặc đã quá 7 ngày yêu cầu trả hàng.");
        if (await db.ReturnRequests.AnyAsync(x => x.OrderId == orderId, ct))
            throw new InvalidOperationException("Đơn đã có yêu cầu trả hàng. Hãy theo dõi yêu cầu hiện tại.");
        if (await db.Disputes.AnyAsync(x => x.OrderId == orderId && x.WorkflowEnabled && x.IsOpen, ct))
            throw new InvalidOperationException("Đơn đang có tranh chấp. Hãy xử lý trả hàng trong hồ sơ đó.");
        if (await db.Refunds.AnyAsync(x => x.OrderId == orderId && x.Reason != "shipping-adjustment", ct))
            throw new InvalidOperationException("Đơn đã có giao dịch hoàn tiền khác; cần xử lý riêng.");
        return await BuildMoneyAsync(db, order, reasonCode, label, ct);
    }

    public static async Task<ReturnQuote> BuildAgreementAsync(ApplicationDbContext db, OrderTable order, CancellationToken ct)
    {
        var quote = await BuildMoneyAsync(db, order, "WrongItem", "Trả hàng theo thỏa thuận hoàn toàn bộ", ct);
        return quote with { ReasonCode = "AgreementFullRefund" };
    }

    public static bool HasRouteSnapshot(OrderTable order) => order.TotalWeightKg is > 0
        && ((!string.IsNullOrWhiteSpace(order.PickupStateSnapshot) && !string.IsNullOrWhiteSpace(order.PickupCountrySnapshot))
            || (order.PickupAddressSnapshot ?? "").Split(',').Length >= 5)
        && ((!string.IsNullOrWhiteSpace(order.DeliveryStateSnapshot) && !string.IsNullOrWhiteSpace(order.DeliveryCountrySnapshot))
            || (order.AddressSnapshot ?? "").Split(',').Length >= 5);

    private static async Task<ReturnQuote> BuildMoneyAsync(ApplicationDbContext db, OrderTable order, string reasonCode, string label, CancellationToken ct)
    {
        var orderId = order.Id;
        var payment = await db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == orderId && x.Status == "Succeeded", ct);
        var paid = payment.Amount ?? throw new InvalidOperationException("Chưa có snapshot số tiền đã thanh toán.");
        if (paid != order.TotalPrice || paid < 0 || paid == 0 && !(payment.Method == "Promotion" && order.PricingSchemaVersion == 1))
            throw new InvalidOperationException("Số tiền thanh toán không khớp snapshot đơn.");
        var weight = order.TotalWeightKg ?? throw new InvalidOperationException("Đơn cũ thiếu khối lượng, không thể báo phí trả hàng.");
        var pickup = Region(order.PickupStateSnapshot, order.PickupCountrySnapshot, order.PickupAddressSnapshot);
        var delivery = Region(order.DeliveryStateSnapshot, order.DeliveryCountrySnapshot, order.AddressSnapshot);
        if (string.IsNullOrWhiteSpace(order.AddressSnapshot) || string.IsNullOrWhiteSpace(order.PickupAddressSnapshot))
            throw new InvalidOperationException("Đơn thiếu snapshot địa chỉ để tạo vận đơn trả.");
        var returnFee = ShippingPricing.Calculate(weight, ShippingPricing.IsHanoiLocal(delivery.State, delivery.Country, pickup.State, pickup.Country));
        var shippingRefunds = await db.Refunds.Where(x => x.OrderId == orderId && x.Status == "Succeeded" && x.Reason == "shipping-adjustment")
            .SumAsync(x => x.Amount, ct);
        var subsidy = order.PricingSchemaVersion == 1 ? order.PlatformSubsidy : 0m;
        var price = ReturnPricing.Calculate(reasonCode, paid, order.ShippingFee, shippingRefunds, subsidy, returnFee);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            version = 1, orderId, payment.Id, reasonCode, paid, shippingRefunds, price, weight,
            order.AddressSnapshot, order.PickupAddressSnapshot, order.Currency
        }))));
        return new(orderId, reasonCode, label, price.ShippingPayer, paid, shippingRefunds, price.GoodsPaid,
            price.OutboundShippingRetained, price.ReturnShippingFee, price.BuyerReturnShippingFee, price.BuyerRefund,
            price.PlatformSubsidyReversal, price.SellerGrossRefund, weight, order.AddressSnapshot,
            order.PickupAddressSnapshot, order.Currency, price.CanRequest,
            price.CanRequest ? null : "Phí trả hàng lớn hơn tiền hàng có thể hoàn. Cần xử lý riêng trước khi tạo vận đơn.", fingerprint);
    }

    private static (string State, string Country) Region(string? state, string? country, string? snapshot)
    {
        if (!string.IsNullOrWhiteSpace(state) && !string.IsNullOrWhiteSpace(country)) return (state, country);
        var parts = (snapshot ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5) throw new InvalidOperationException("Đơn cũ thiếu vùng địa chỉ snapshot, không thể tính phí trả hàng.");
        return (parts[^2], parts[^1]);
    }
}
