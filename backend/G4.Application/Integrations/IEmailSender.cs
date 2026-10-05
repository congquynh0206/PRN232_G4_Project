namespace G4.Application.Integrations;

public sealed record EmailEnvelope(int NotificationId, string MessageId, string From,
    string Recipient, string Subject, string TextBody, string? HtmlBody);

public interface IEmailSender
{
    Task SendAsync(EmailEnvelope email, CancellationToken ct);
}
