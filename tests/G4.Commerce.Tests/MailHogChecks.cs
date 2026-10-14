using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using G4.Domain.Entities;
using G4.Infrastructure.Notifications;
using G4.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

public static class MailHogChecks
{
    public static async Task RunAsync()
    {
        using var http = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:8025"),
            Timeout = TimeSpan.FromSeconds(10)
        };
        using var available = await http.GetAsync("api/v2/messages?limit=1");
        available.EnsureSuccessStatusCode();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mail:Mode"] = "Smtp",
            ["Mail:Host"] = "127.0.0.1",
            ["Mail:Port"] = "1025",
            ["Mail:EnableSsl"] = "false"
        }).Build();
        var factory = new NotificationProcessorBehaviorChecks.Factory(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options
        );
        var clock = new NotificationProcessorBehaviorChecks.Clock();
        var writer = new IntegrationDiagnosticsChecks.Writer();
        var processor = new NotificationProcessor(factory, new SmtpEmailSender(config), config, clock, writer);
        await using var db = factory.CreateDbContext();
        var mail = new NotificationOutbox
        {
            Id = Random.Shared.Next(100_000_000, 1_000_000_000),
            OrderId = 17,
            EventType = "PaymentSucceeded",
            From = "g4@example.test",
            Recipient = "buyer@example.test",
            Subject = "MailHog QA - Thanh toán thành công",
            Body = "Đơn hàng #17 đã thanh toán 27.50 USD.",
            HtmlBody = "<p>Đơn hàng <strong>#17</strong> đã thanh toán <b>27.50 USD</b>.</p>",
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };
        db.NotificationOutbox.Add(mail);
        await db.SaveChangesAsync();
        await processor.ProcessDueAsync(1, default);
        await db.Entry(mail).ReloadAsync();
        if (mail.Status != "Sent" || mail.SentAt == null || mail.CapturedAt != null)
            throw new Exception("MailHog SMTP must produce Sent with SentAt, not Captured");
        await CheckReceivedAsync(http, mail);

        // Use a closed local port to test failure without stopping the user's MailHog.
        var vacant = new TcpListener(IPAddress.Loopback, 0);
        vacant.Start();
        var closedPort = ((IPEndPoint)vacant.LocalEndpoint).Port;
        vacant.Stop();
        config["Mail:Port"] = closedPort.ToString();
        var retry = new NotificationOutbox
        {
            Id = mail.Id + 1,
            OrderId = 18,
            EventType = "RefundSucceeded",
            From = mail.From,
            Recipient = mail.Recipient,
            Subject = "MailHog QA - Gửi lại thành công",
            Body = "Đã hoàn tiền 12.00 USD cho đơn hàng #18.",
            HtmlBody = "<p>Đã hoàn tiền <b>12.00 USD</b> cho đơn hàng #18.</p>",
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };
        db.NotificationOutbox.Add(retry);
        await db.SaveChangesAsync();
        await processor.ProcessDueAsync(1, default);
        await db.Entry(retry).ReloadAsync();
        if (retry.Status != "Pending" || retry.Attempts != 1 || retry.SentAt != null)
            throw new Exception("SMTP connection failure must schedule retry without claiming Sent");
        await processor.ProcessDueAsync(1, default);
        clock.Advance(30);
        await processor.ProcessDueAsync(1, default);
        clock.Advance(120);
        await processor.ProcessDueAsync(1, default);
        await db.Entry(retry).ReloadAsync();
        if (retry.Status != "Failed" || retry.Attempts != 3 || retry.CapturedAt != null)
            throw new Exception("SMTP failure must stop after three attempts without Capture fallback");

        config["Mail:Port"] = "1025";
        if (!await processor.RetryFailedAsync(retry.Id, default))
            throw new Exception("Failed email could not start a new retry cycle");
        await processor.ProcessDueAsync(1, default);
        await db.Entry(retry).ReloadAsync();
        if (retry.Status != "Sent" || retry.Attempts != 4 || retry.Cycle != 2)
            throw new Exception("SMTP retry did not preserve attempts and complete a new cycle");
        await CheckReceivedAsync(http, retry);
        if (writer.Entries.Count != 5 || writer.Entries.Count(x => x.Outcome == "Succeeded") != 2)
            throw new Exception("SMTP diagnostic history must include three failures and two successes");

        Console.WriteLine("MailHog real SMTP: recipient, Message-ID, UTF-8 text/HTML, Sent, failure limit and retry passed.");
        Console.WriteLine("Two QA emails are available at http://localhost:8025. No SQL database was modified.");
    }

    private static async Task CheckReceivedAsync(HttpClient http, NotificationOutbox mail)
    {
        var messageId = $"<g4-notification-{mail.Id}@g4.example.test>";
        using var response = await http.GetAsync(
            "api/v2/search?kind=containing&query=" + Uri.EscapeDataString(messageId)
        );
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = result.RootElement.GetProperty("items");
        if (items.GetArrayLength() != 1)
            throw new Exception("MailHog must receive exactly one email for the notification Message-ID");
        var received = items[0];
        var recipient = received.GetProperty("To")[0];
        if (recipient.GetProperty("Mailbox").GetString() != "buyer" ||
            recipient.GetProperty("Domain").GetString() != "example.test")
            throw new Exception("MailHog received an incorrect SMTP recipient");

        var id = received.GetProperty("ID").GetString()!;
        var raw = await http.GetStringAsync("api/v1/messages/" + Uri.EscapeDataString(id) + "/download");
        var parts = Regex.Matches(raw,
            @"Content-Type: text/(plain|html);[^\r\n]*\r?\nContent-Transfer-Encoding: base64\r?\n\r?\n([A-Za-z0-9+/=\r\n]+)");
        var bodies = parts.Select(part => Encoding.UTF8.GetString(
            Convert.FromBase64String(part.Groups[2].Value)
        )).ToArray();
        if (!raw.Contains(messageId) || parts.Count != 2 ||
            !bodies.Contains(mail.Body) || !bodies.Contains(mail.HtmlBody))
            throw new Exception("MailHog MIME must preserve Message-ID and exact UTF-8 text/HTML bodies");
    }
}
