using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Notifications;

// Adds rows to the caller's unit of work. Sending is exclusively handled by NotificationProcessor.
public sealed class BusinessNotificationService(
    ApplicationDbContext db,
    IConfiguration? config = null,
    TimeProvider? clock = null
)
{
    public async Task PaymentFailedAsync(
        OrderTable order,
        Payment payment,
        CancellationToken ct = default
    )
    {
        if (
            payment.Id <= 0
            || payment.OrderId != order.Id
            || payment.Status is not ("Failed" or "Declined" or "Expired" or "Invalid")
        )
            return;
        var reason =
            payment.Method == "Card"
                ? payment.Status switch
                {
                    "Expired" => "Thẻ giả lập đã hết hạn. Hãy chọn thông tin thẻ hợp lệ.",
                    "Invalid" => "Thông tin thẻ giả lập chưa hợp lệ. Vui lòng kiểm tra lại.",
                    _ => "Thẻ giả lập bị từ chối. Bạn có thể thử phương thức khác.",
                }
                : "PayPal không chấp nhận lần thanh toán này. Vui lòng kiểm tra và thử lại nếu đơn còn hạn.";
        await AddAsync(
            order.BuyerId,
            null,
            order.Id,
            null,
            "PaymentFailed",
            $"payment:{payment.Id}:failed",
            "buyer",
            payment.Amount,
            order.Currency,
            payment.Method,
            $"Lần thanh toán #{payment.Id}. {reason}",
            null,
            ct
        );
    }

    public async Task CancellationAsync(
        OrderTable order,
        bool immediate,
        CancellationToken ct = default
    )
    {
        if (
            immediate && order.Status != "Cancelled"
            || !immediate && order.Status != "CancelRequested"
        )
            throw new InvalidOperationException("Chưa có kết quả hủy đơn để tạo thông báo.");
        var type = immediate ? "OrderCancelled" : "CancellationRequested";
        var key = $"order:{order.Id}:cancel:{order.CancelRequestNumber}:{type}";
        await AddAsync(
            order.BuyerId,
            null,
            order.Id,
            null,
            type,
            key,
            "buyer",
            null,
            order.Currency,
            null,
            null,
            null,
            ct
        );
        if (!immediate)
            await AddAsync(
                order.SellerId,
                order.SellerId,
                order.Id,
                null,
                type,
                key,
                "seller",
                null,
                order.Currency,
                null,
                null,
                null,
                ct
            );
    }

    public async Task ReturnAsync(
        ReturnRequest request,
        string type,
        CancellationToken ct = default
    )
    {
        if (request.Id <= 0 || request.OrderId == null)
            throw new InvalidOperationException("Yêu cầu trả hàng chưa được lưu.");
        if (type is not ("ReturnRequested" or "ReturnApproved" or "ReturnRejected"))
            throw new ArgumentException("Loại thông báo trả hàng không hợp lệ.");
        if (
            type == "ReturnRequested" && request.Status != "Requested"
            || type == "ReturnApproved" && request.Status is not ("Approved" or "ReturnShipping")
            || type == "ReturnRejected" && request.Status != "Rejected"
        )
            throw new InvalidOperationException("Trạng thái trả hàng không khớp thông báo.");
        var order = await db.OrderTables.SingleAsync(x => x.Id == request.OrderId, ct);
        var reason = type == "ReturnRejected" ? request.DecisionReason : request.Reason;
        await AddAsync(
            order.BuyerId,
            null,
            order.Id,
            null,
            type,
            $"return:{request.Id}:{type}",
            "buyer",
            null,
            order.Currency,
            null,
            reason,
            null,
            ct
        );
        if (type == "ReturnRequested")
            await AddAsync(
                order.SellerId,
                order.SellerId,
                order.Id,
                null,
                type,
                $"return:{request.Id}:{type}",
                "seller",
                null,
                order.Currency,
                null,
                reason,
                null,
                ct
            );
    }

    public async Task ShipmentReattemptAsync(
        OrderTable order,
        ShippingInfo shipment,
        string eventId,
        CancellationToken ct = default
    )
    {
        var route = shipment.Direction == "Return" ? "Chiều trả về seller" : "Chiều giao tới buyer";
        var reason =
            $"{route}. Dự kiến: {shipment.ReattemptAt:O} (UTC). Lý do: {shipment.ReattemptReason}";
        var key = $"shipment:{shipment.Id}:reattempt:{eventId}";
        await AddAsync(
            order.BuyerId,
            null,
            order.Id,
            null,
            "ReattemptScheduled",
            key,
            "buyer",
            null,
            order.Currency,
            null,
            reason,
            null,
            ct
        );
        await AddAsync(
            order.SellerId,
            order.SellerId,
            order.Id,
            null,
            "ReattemptScheduled",
            key,
            "seller",
            null,
            order.Currency,
            null,
            reason,
            null,
            ct
        );
    }

    public async Task PayoutAsync(SellerPayout payout, int sellerId, CancellationToken ct = default)
    {
        if (payout.Id <= 0 || payout.Status is not ("Completed" or "Returned"))
            return;
        if (
            !await db.SellerAccounts.AnyAsync(
                a => a.Id == payout.SellerAccountId && a.SellerId == sellerId,
                ct
            )
        )
            throw new InvalidOperationException("Tài khoản payout không khớp người nhận.");
        var type = payout.Status == "Completed" ? "PayoutCompleted" : "PayoutReturned";
        await AddAsync(
            sellerId,
            sellerId,
            null,
            payout.Id,
            type,
            $"payout:{payout.Id}:{type}",
            "seller",
            payout.Amount,
            payout.Currency,
            null,
            null,
            payout.BankReferenceId,
            ct
        );
    }

    private async Task AddAsync(
        int? userId,
        int? sellerId,
        int? orderId,
        int? payoutId,
        string type,
        string identity,
        string audience,
        decimal? amount,
        string currency,
        string? method,
        string? reason,
        string? reference,
        CancellationToken ct
    )
    {
        if (userId == null)
            return; // Legacy orphan orders have no recipient; never send to a guessed address.
        var key = $"n1:{identity}:user:{userId}";
        if (
            db.NotificationOutbox.Local.Any(x => x.EventKey == key)
            || await db.NotificationOutbox.AnyAsync(x => x.EventKey == key, ct)
        )
            return;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user == null)
            return;
        var recipient = user.Email ?? "";
        string? link = null;
        if (
            Uri.TryCreate(config?["Frontend:PublicBaseUrl"], UriKind.Absolute, out var origin)
            && origin.Scheme is "http" or "https"
        )
            link =
                origin.AbsoluteUri.TrimEnd('/')
                + (
                    payoutId != null
                        ? "/Seller?tab=finance"
                        : $"/{(audience == "seller" ? "Seller" : "Buyer")}?orderId={orderId}"
                );
        var content = NotificationTemplates.BuildBusiness(
            new(
                type,
                orderId,
                payoutId,
                audience,
                amount,
                currency,
                method,
                reason,
                reference,
                link
            )
        );
        var valid = NotificationService.IsAddress(recipient);
        db.NotificationOutbox.Add(
            new NotificationOutbox
            {
                OrderId = orderId,
                SellerId = sellerId,
                RecipientUserId = userId,
                PayoutId = payoutId,
                EventKey = key,
                EventType = type,
                Recipient = recipient,
                From = config?["Mail:From"] ?? "g4@example.test",
                Subject = content.Subject,
                Body = content.TextBody,
                HtmlBody = content.HtmlBody,
                CreatedAt = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime,
                Status = valid ? "Pending" : "Failed",
                LastErrorCode = valid ? null : "RecipientInvalid",
                LastErrorSummary = valid ? null : "Địa chỉ email người nhận chưa hợp lệ.",
            }
        );
    }
}
