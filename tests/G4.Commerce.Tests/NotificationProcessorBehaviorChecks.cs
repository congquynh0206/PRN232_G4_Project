using G4.Infrastructure.Notifications;
using G4.Application.Integrations;
using G4.Infrastructure.Persistence;
using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

public static class NotificationProcessorBehaviorChecks
{
    public static async Task RunAsync()
    {
        var factory = new Factory(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var clock = new Clock();
        var sender = new Sender();
        var writer = new Writer();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Mail:Mode"]="Smtp" }).Build();
        await using var db = factory.CreateDbContext();
        db.NotificationOutbox.Add(new NotificationOutbox { Id=1, OrderId=4, EventType="Delivered", Recipient="buyer@example.test", From="g4@example.test", CreatedAt=clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync();
        var processor = new NotificationProcessor(factory, sender, config, clock, writer);
        sender.Fail = true;
        await processor.ProcessDueAsync(20, default);
        await processor.ProcessDueAsync(20, default);
        if(sender.Calls!=1) throw new Exception("Retry sent before 30s");
        clock.Advance(30);
        await processor.ProcessDueAsync(20, default);
        clock.Advance(119);
        await processor.ProcessDueAsync(20, default);
        if(sender.Calls!=2) throw new Exception("Retry sent before 120s");
        clock.Advance(1);
        await processor.ProcessDueAsync(20, default);
        await db.Entry(db.NotificationOutbox.Local.Single()).ReloadAsync();
        var mail=db.NotificationOutbox.Local.Single();
        if(mail.Status!="Failed" || mail.Attempts!=3 || writer.Entries.Count!=3) throw new Exception("Retry cycle did not stop and log all three attempts");
        if(!await processor.RetryFailedAsync(1,default) || await processor.RetryFailedAsync(1,default)) throw new Exception("Duplicate manual retry accepted");
        sender.Fail=false;
        await processor.ProcessDueAsync(20,default);
        await db.Entry(mail).ReloadAsync();
        if(mail.Status!="Sent" || mail.Attempts!=4 || mail.Cycle!=2 || mail.SentAt==null || await processor.RetryFailedAsync(1,default)) throw new Exception("Sent status/manual cycle incorrect");
        config["Mail:Mode"]="Capture";
        db.NotificationOutbox.Add(new NotificationOutbox { Id=2, OrderId=5, EventType="Delivered", Recipient="buyer@example.test" });
        await db.SaveChangesAsync();
        await processor.ProcessDueAsync(20,default);
        await db.Entry(db.NotificationOutbox.Local.Single(x=>x.Id==2)).ReloadAsync();
        var captured=db.NotificationOutbox.Local.Single(x=>x.Id==2);
        if(captured.Status!="Captured" || captured.CapturedAt==null || captured.SentAt!=null || sender.Calls!=4) throw new Exception("Capture must not send SMTP or claim Sent");
        db.NotificationOutbox.Add(new NotificationOutbox { Id=3, OrderId=6, EventType="Delivered", Recipient="", Status="Failed", LastErrorCode="RecipientInvalid" });
        await db.SaveChangesAsync();
        await processor.RetryFailedAsync(3,default);
        await processor.ProcessDueAsync(20,default);
        await db.Entry(db.NotificationOutbox.Local.Single(x=>x.Id==3)).ReloadAsync();
        if(db.NotificationOutbox.Local.Single(x=>x.Id==3).Status!="Failed")throw new Exception("Capture cleared invalid recipient on retry");
        if(writer.Entries.Take(3).Any(x=>x.Outcome!="Unknown"))throw new Exception("SMTP connection failure falsely treated as certain non-delivery");
        Console.WriteLine("Notification processor Capture, retry delays, limits, manual cycle and history passed");
    }
    public sealed class Factory(DbContextOptions<ApplicationDbContext> options):IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext()=>new(options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken ct=default)=>Task.FromResult(CreateDbContext());
    }
    public sealed class Clock:TimeProvider
    {
        private DateTimeOffset now=new(2026,10,5,0,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>now;
        public void Advance(int seconds)=>now=now.AddSeconds(seconds);
    }
    private sealed class Sender:IEmailSender
    {
        public int Calls; public bool Fail;
        public Task SendAsync(EmailEnvelope mail,CancellationToken ct) { Calls++; if(Fail)throw new System.Net.Mail.SmtpException("secret-canary"); return Task.CompletedTask; }
    }
    private sealed class Writer:IIntegrationLogWriter
    {
        public List<IntegrationLogEntry> Entries=new();
        public Task WriteAsync(IntegrationLogEntry entry,CancellationToken ct) { Entries.Add(entry); return Task.CompletedTask; }
    }
}
