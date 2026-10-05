using System.Globalization;
using System.Net;

namespace G4.Infrastructure.Notifications;

public sealed record NotificationContent(string Subject, string TextBody, string HtmlBody);
public sealed record NotificationTemplateInput(int OrderId, string EventType, decimal? Amount,
    string Currency, string? Method, string? TrackingNumber, string? Reason,
    string? ProviderReference, string? OrderUrl);

public static class NotificationTemplates
{
    public static NotificationContent Build(NotificationTemplateInput input)
    {
        var title = input.EventType switch
        {
            "PaymentSucceeded" => "Thanh toán thành công",
            "Delivered" => "Đơn hàng đã được giao",
            "DeliveryFailed" => "Giao hàng chưa thành công",
            "Refunded" => "Hoàn tiền thành công",
            _ => throw new ArgumentException("Loại thông báo không hợp lệ.")
        };
        var lines = new List<string> { $"Đơn hàng #{input.OrderId}", title };
        if (input.Amount is not null) lines.Add($"Số tiền: {input.Amount.Value.ToString("F2", CultureInfo.InvariantCulture)} {input.Currency}");
        if (input.Method is not null) lines.Add("Phương thức: " + (input.Method switch
        { "Card" => "Thẻ giả lập", "PayPal" => "PayPal Sandbox", "Promotion" => "Thanh toán bằng khuyến mãi", _ => "Chưa có" }));
        if (!string.IsNullOrWhiteSpace(input.TrackingNumber)) lines.Add($"Mã vận đơn: {input.TrackingNumber}");
        if (!string.IsNullOrWhiteSpace(input.ProviderReference)) lines.Add($"Mã giao dịch: {input.ProviderReference}");
        if (!string.IsNullOrWhiteSpace(input.Reason)) lines.Add($"Thông tin: {input.Reason}");
        lines.Add(input.EventType switch
        {
            "Delivered" => "Bạn có thể xem đơn hàng và yêu cầu hỗ trợ nếu hàng nhận được có vấn đề.",
            "DeliveryFailed" => "Vui lòng theo dõi vận đơn để biết lịch giao lại hoặc liên hệ hỗ trợ.",
            "Refunded" => "Khoản hoàn đã được xử lý thành công. Thời gian hiển thị phụ thuộc phương thức thanh toán.",
            _ => "Bạn có thể theo dõi quá trình giao hàng trong Đơn của tôi."
        });
        var html = "<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><body style=\"font-family:Arial,sans-serif;color:#183153;padding:24px\">" +
            $"<h2>{WebUtility.HtmlEncode(title)}</h2>" + string.Join("", lines.Select(l => $"<p>{WebUtility.HtmlEncode(l)}</p>"));
        if (Uri.TryCreate(input.OrderUrl, UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
        {
            lines.Add($"Xem đơn hàng: {url.AbsoluteUri}");
            html += $"<p><a href=\"{WebUtility.HtmlEncode(url.AbsoluteUri)}\">Xem đơn hàng</a></p>";
        }
        return new NotificationContent($"{title} — Đơn #{input.OrderId}", string.Join("\n", lines), html + "</body></html>");
    }
}
