using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Net.Mail;

namespace G4.Infrastructure.Notifications;

public sealed class NotificationService(ApplicationDbContext db, IConfiguration? config = null)
{
    public async Task EnqueueAsync(OrderTable order, string eventType, CancellationToken ct)
    {
        if (db.NotificationOutbox.Local.Any(x => x.OrderId == order.Id && x.EventType == eventType) ||
            await db.NotificationOutbox.AnyAsync(x => x.OrderId == order.Id && x.EventType == eventType, ct)) return;
        var recipient = await db.Users.Where(x => x.Id == order.BuyerId).Select(x => x.Email).SingleOrDefaultAsync(ct) ?? "";
        var payment = db.Payments.Local.Where(x => x.OrderId == order.Id && x.Status == "Succeeded").OrderByDescending(x=>x.Id).FirstOrDefault()
            ?? await db.Payments.Where(x => x.OrderId == order.Id && x.Status == "Succeeded").OrderByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
        var refund = eventType == "Refunded" ? db.Refunds.Local.Where(x=>x.OrderId==order.Id && x.Status=="Succeeded").OrderByDescending(x=>x.Id).FirstOrDefault()
            ?? await db.Refunds.Where(x=>x.OrderId==order.Id && x.Status=="Succeeded").OrderByDescending(x=>x.Id).FirstOrDefaultAsync(ct) : null;
        if (eventType == "PaymentSucceeded" && payment == null || eventType == "Refunded" && refund == null)
            throw new InvalidOperationException("Không tạo email thành công khi giao dịch chưa hoàn tất.");
        var shipment = eventType is "Delivered" or "DeliveryFailed" ? db.ShippingInfos.Local.FirstOrDefault(x=>x.OrderId==order.Id && x.Direction=="Outbound")
            ?? await db.ShippingInfos.FirstOrDefaultAsync(x=>x.OrderId==order.Id && x.Direction=="Outbound", ct) : null;
        var baseUrl = config?["Frontend:PublicBaseUrl"];
        var link = Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin) && origin.Scheme is "http" or "https"
            ? origin.AbsoluteUri.TrimEnd('/') + $"/Buyer?orderId={order.Id}" : null;
        var content = NotificationTemplates.Build(new(order.Id, eventType,
            eventType == "PaymentSucceeded" ? payment?.Amount : refund?.Amount, order.Currency,
            eventType is "PaymentSucceeded" or "Refunded" ? payment?.Method : null, shipment?.TrackingNumber,
            eventType == "DeliveryFailed" ? shipment?.FailureReason : null, refund?.ProviderRefundId ??
            (eventType == "PaymentSucceeded" ? payment?.ProviderTransactionId : null), link));
        var validRecipient = IsAddress(recipient);
        db.NotificationOutbox.Add(new NotificationOutbox
        {
            OrderId = order.Id, EventType = eventType, Recipient = recipient, From = config?["Mail:From"] ?? "g4@example.test",
            Subject = content.Subject, Body = content.TextBody, HtmlBody = content.HtmlBody, CreatedAt = DateTime.UtcNow,
            Status = validRecipient ? "Pending" : "Failed", LastErrorCode = validRecipient ? null : "RecipientInvalid",
            LastErrorSummary = validRecipient ? null : "Địa chỉ email người nhận chưa hợp lệ."
        });
    }

    internal static bool IsAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Contains('\r') || address.Contains('\n')) return false;
        try { return new MailAddress(address).Address.Equals(address, StringComparison.OrdinalIgnoreCase); }
        catch (FormatException) { return false; }
    }
}
