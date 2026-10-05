using G4.Infrastructure.Notifications;
using G4.Application.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
public static class SmtpEmailChecks
{
    public static async Task RunAsync()
    {
        await using var smtp=new FakeSmtpServer();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Mail:Host"]="127.0.0.1",["Mail:Port"]=smtp.Port.ToString() }).Build();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await new SmtpEmailSender(config).SendAsync(new(17,"<g4-notification-17@g4.example.test>","g4@example.test","buyer@example.test","SMTP_TEST","SMTP_BODY_TEST","<p>SMTP_HTML_TEST</p>"),timeout.Token);
        var received=await smtp.Received.WaitAsync(timeout.Token);
        Directory.CreateDirectory("artifacts/email-integration");
        await File.WriteAllTextAsync("artifacts/email-integration/smtp-test-message.eml",received);
        var parts=System.Text.RegularExpressions.Regex.Matches(received,@"Content-Type: text/(plain|html);[^\r\n]*\r?\nContent-Transfer-Encoding: base64\r?\n\r?\n([A-Za-z0-9+/=\r\n]+)");
        var decoded=parts.Select(part=>System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(part.Groups[2].Value))).ToArray();
        if(!received.Contains("g4-notification-17@g4.example.test")||parts.Count!=2||!decoded.Contains("SMTP_BODY_TEST")||!decoded.Contains("<p>SMTP_HTML_TEST</p>")) throw new Exception("SMTP must receive stable Message-ID and exactly two decoded text/HTML alternatives");
        await CheckFailureAsync("reject","Failed");
        await CheckFailureAsync("unavailable","Unknown");
        await using var slow=new FakeSmtpServer("timeout");
        config["Mail:Port"]=slow.Port.ToString();
        using var shortBudget=new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try { await new SmtpEmailSender(config).SendAsync(new(17,"<g4-notification-17@g4.example.test>","g4@example.test","buyer@example.test","TEST","BODY",null),shortBudget.Token);throw new Exception("SMTP timeout accepted"); }
        catch(OperationCanceledException) when(shortBudget.IsCancellationRequested) { }
        Console.WriteLine("Real loopback SMTP text/HTML, stable Message-ID, 451, connection refusal and timeout checks passed");
    }
    private static async Task CheckFailureAsync(string behavior,string outcome)
    {
        await using var server=behavior=="reject" ? new FakeSmtpServer(behavior) : null;
        int port;
        if(server!=null)port=server.Port;
        else { var vacant=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);vacant.Start();port=((System.Net.IPEndPoint)vacant.LocalEndpoint).Port;vacant.Stop(); }
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Mail:Mode"]="Smtp",["Mail:Host"]="127.0.0.1",["Mail:Port"]=port.ToString() }).Build();
        var options=new DbContextOptionsBuilder<G4.Infrastructure.Persistence.ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var factory=new NotificationProcessorBehaviorChecks.Factory(options);
        await using var db=factory.CreateDbContext();
        db.NotificationOutbox.Add(new G4.Domain.Entities.NotificationOutbox {OrderId=17,EventType="Delivered",Recipient="buyer@example.test",From="g4@example.test"});await db.SaveChangesAsync();
        var writer=new IntegrationDiagnosticsChecks.Writer();
        await new NotificationProcessor(factory,new SmtpEmailSender(config),config,TimeProvider.System,writer).ProcessDueAsync(1,default);
        if(writer.Entries.Single().Outcome!=outcome)throw new Exception("SMTP delivery certainty incorrect for "+behavior);
    }
}
