using System.Globalization;
using System.Net;

namespace G4.Infrastructure.Notifications;
public sealed record NotificationContent(string Subject, string TextBody, string HtmlBody);
public sealed record BusinessNotificationInput(string EventType, int? OrderId, int? PayoutId, string Audience,
    decimal? Amount, string Currency, string? Method, string? Reason, string? ProviderReference, string? Url);
public sealed record NotificationTemplateInput(
    int OrderId,
    string EventType,
    decimal? Amount,
    string Currency,
    string? Method,
    string? TrackingNumber,
    string? Reason,
    string? ProviderReference,
    string? OrderUrl
);
public static class NotificationTemplates
{
    public static NotificationContent BuildBusiness(BusinessNotificationInput input)
    {
        var seller = input.Audience == "seller";
        var (title, next) = input.EventType switch {
            "PaymentFailed" => ("Thanh toán chưa thành công", "Đơn chưa được ghi nhận thanh toán thành công từ lần thử này. Mở đơn để kiểm tra thời hạn và thử lại."),
            "OrderCancelled" => ("Đã hủy đơn hàng", "Đơn chưa thanh toán đã được hủy. Không có khoản thanh toán cần hoàn từ thao tác hủy này."),
            "CancellationRequested" => ("Đã tiếp nhận yêu cầu hủy đơn", seller ? "Người mua yêu cầu hủy. Vui lòng mở đơn để duyệt hoặc từ chối; đơn chưa được coi là đã hủy/hoàn tiền." : "Yêu cầu đã được gửi tới shop để xét duyệt. Đơn chưa được hủy và chưa được hoàn tiền chỉ từ yêu cầu này."),
            "ReturnRequested" => ("Đã tiếp nhận yêu cầu trả hàng", seller ? "Vui lòng xem lý do và duyệt hoặc từ chối yêu cầu trong đơn hàng." : "Shop sẽ xem xét yêu cầu. Chưa cần gửi hàng khi chưa được duyệt; chưa có khoản hoàn tiền thành công từ yêu cầu này."),
            "ReturnApproved" => ("Yêu cầu trả hàng đã được duyệt", "Mở đơn để xem vận đơn chiều trả và bước gửi hàng. Nếu đang chờ tạo vận đơn hoặc tạo nhãn lỗi, hãy chờ shop xử lý. Khoản hoàn được xử lý theo kết quả trả hàng, chưa hoàn ngay khi duyệt."),
            "ReturnRejected" => ("Yêu cầu trả hàng bị từ chối", "Xem lý do trong đơn. Nếu không đồng ý, bạn có thể mở tranh chấp trong thời hạn quy định; việc từ chối chưa tạo khoản hoàn tiền."),
            "PayoutCompleted" => ("Rút tiền hoàn tất", "Ngân hàng giả lập đã hoàn tất nhận khoản rút. Xem lịch sử rút tiền để đối chiếu; đây không phải doanh thu mới."),
            "PayoutReturned" => ("Ngân hàng giả lập trả lại khoản rút", "Khoản rút không hoàn tất và tiền đã được trả về hệ thống, bù nợ nếu có. Kiểm tra ví và lịch sử rút tiền để biết số dư khả dụng."),
            _ => throw new ArgumentException("Loại thông báo nghiệp vụ không hợp lệ.")
        };
        var context = input.PayoutId is { } payout ? $"Lần rút tiền #{payout}" : $"Đơn hàng #{input.OrderId}";
        var lines = new List<string> { context, title };
        if (input.Amount != null) lines.Add($"Số tiền: {input.Amount.Value.ToString("F2", CultureInfo.InvariantCulture)} {input.Currency}");
        if (input.Method != null) lines.Add("Phương thức: " + (input.Method == "PayPal" ? "PayPal Sandbox" : "Thẻ giả lập"));
        if (!string.IsNullOrWhiteSpace(input.Reason)) lines.Add("Thông tin: " + input.Reason);
        if (!string.IsNullOrWhiteSpace(input.ProviderReference)) lines.Add("Mã tham chiếu: " + input.ProviderReference);
        lines.Add(next);
        var html = "<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><body style=\"font-family:Arial,sans-serif;color:#183153;padding:24px\">"
            + $"<h2>{WebUtility.HtmlEncode(title)}</h2>" + string.Join("", lines.Select(l => $"<p style=\"white-space:pre-wrap\">{WebUtility.HtmlEncode(l)}</p>"));
        if (Uri.TryCreate(input.Url, UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
        {
            var label = input.PayoutId != null ? "Xem tài chính và lịch sử rút tiền" : "Xem đơn hàng";
            lines.Add($"{label}: {url.AbsoluteUri}"); html += $"<p><a href=\"{WebUtility.HtmlEncode(url.AbsoluteUri)}\">{label}</a></p>";
        }
        return new($"{title} — {context}", string.Join("\n", lines), html + "</body></html>");
    }

    public static NotificationContent Build(NotificationTemplateInput input)
    {
        var title = input.EventType switch
        {
            "PaymentSucceeded" => "Thanh toán thành công",
            "Delivered" => "Đơn hàng đã được giao",
            "DeliveryFailed" => "Giao hàng chưa thành công",
            "Refunded" => "Hoàn tiền thành công",
            _ => throw new ArgumentException("Loại thông báo không hợp lệ.")};
        var lines = new List<string>
        {
            $"Đơn hàng #{input.OrderId}",
            title
        };
        if (input.Amount is not null)
            lines.Add($"Số tiền: {input.Amount.Value.ToString("F2", CultureInfo.InvariantCulture)} {input.Currency}");
        if (input.Method is not null)
            lines.Add("Phương thức: " + (input.Method switch
            {
                "Card" => "Thẻ giả lập",
                "PayPal" => "PayPal Sandbox",
                "Promotion" => "Thanh toán bằng khuyến mãi",
                _ => "Chưa có"
            }));
        if (!string.IsNullOrWhiteSpace(input.TrackingNumber))
            lines.Add($"Mã vận đơn: {input.TrackingNumber}");
        if (!string.IsNullOrWhiteSpace(input.ProviderReference))
            lines.Add($"Mã giao dịch: {input.ProviderReference}");
        if (!string.IsNullOrWhiteSpace(input.Reason))
            lines.Add($"Thông tin: {input.Reason}");
        lines.Add(input.EventType switch
        {
            "Delivered" => "Bạn có thể xem đơn hàng và yêu cầu hỗ trợ nếu hàng nhận được có vấn đề.",
            "DeliveryFailed" => "Vui lòng theo dõi vận đơn để biết lịch giao lại hoặc liên hệ hỗ trợ.",
            "Refunded" => "Khoản hoàn đã được xử lý thành công. Thời gian hiển thị phụ thuộc phương thức thanh toán.",
            _ => "Bạn có thể theo dõi quá trình giao hàng trong Đơn của tôi."
        });
        var html = "<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><body style=\"font-family:Arial,sans-serif;color:#183153;padding:24px\">" + $"<h2>{WebUtility.HtmlEncode(title)}</h2>" + string.Join("", lines.Select(l => $"<p>{WebUtility.HtmlEncode(l)}</p>"));
        if (Uri.TryCreate(input.OrderUrl, UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
        {
            lines.Add($"Xem đơn hàng: {url.AbsoluteUri}");
            html += $"<p><a href=\"{WebUtility.HtmlEncode(url.AbsoluteUri)}\">Xem đơn hàng</a></p>";
        }

        return new NotificationContent($"{title} — Đơn #{input.OrderId}", string.Join("\n", lines), html + "</body></html>");
    }
}
