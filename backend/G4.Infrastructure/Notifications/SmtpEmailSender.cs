using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Notifications;

public sealed class EmailSendException(string code, string summary) : Exception(summary)
{
    public string Code { get; } = code;
}

public sealed class SmtpEmailSender(IConfiguration config) : IEmailSender
{
    public async Task SendAsync(EmailEnvelope email, CancellationToken ct)
    {
        if (!NotificationService.IsAddress(email.Recipient) || !NotificationService.IsAddress(email.From))
            throw new EmailSendException("AddressInvalid", "Địa chỉ email gửi hoặc nhận chưa hợp lệ.");
        var host = config["Mail:Host"];
        if (string.IsNullOrWhiteSpace(host)) throw new EmailSendException("SmtpNotConfigured", "Chưa cấu hình máy chủ email.");
        var port = config.GetValue("Mail:Port", 1025);
        if (port is < 1 or > 65535) throw new EmailSendException("SmtpNotConfigured", "Cổng máy chủ email chưa hợp lệ.");
        using var smtp = new SmtpClient(host, port) { EnableSsl = config.GetValue("Mail:EnableSsl", false) };
        if (!string.IsNullOrWhiteSpace(config["Mail:Username"])) smtp.Credentials = new NetworkCredential(config["Mail:Username"], config["Mail:Password"]);
        using var message = new MailMessage(email.From, email.Recipient, email.Subject, email.TextBody)
        { SubjectEncoding = Encoding.UTF8, BodyEncoding = Encoding.UTF8 };
        message.Headers.Add("Message-ID", email.MessageId);
        if (!string.IsNullOrWhiteSpace(email.HtmlBody))
        {
            message.Body = "";
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(email.TextBody, Encoding.UTF8, "text/plain"));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(email.HtmlBody, Encoding.UTF8, "text/html"));
        }
        await smtp.SendMailAsync(message, ct);
    }
}
