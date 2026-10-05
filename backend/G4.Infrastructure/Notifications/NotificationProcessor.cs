using System.Diagnostics;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Notifications;

public sealed class NotificationProcessor(IDbContextFactory<ApplicationDbContext> factory, IEmailSender sender,
    IConfiguration config, TimeProvider clock, IIntegrationLogWriter? logs = null)
{
    private static readonly SemaphoreSlim InMemoryGate = new(1);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<int> ProcessDueAsync(int batchSize, CancellationToken ct)
    {
        var now = Now;
        await using var db = await factory.CreateDbContextAsync(ct);
        var ids = await db.NotificationOutbox.AsNoTracking().Where(x =>
            x.Status == "Pending" && (x.NextAttemptAt == null || x.NextAttemptAt <= now) ||
            x.Status == "Processing" && x.ProcessingUntil <= now)
            .OrderBy(x=>x.Id).Select(x=>x.Id).Take(Math.Clamp(batchSize,1,100)).ToListAsync(ct);
        var handled = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            await RecoverAsync(db, id, ct);
            var token = Guid.NewGuid();
            var claimed = await MutateAsync(db, id, null, "Pending", row =>
            {
                if (row.NextAttemptAt > Now || row.AttemptsInCycle >= 3) return false;
                row.Status="Processing"; row.ProcessingToken=token; row.ProcessingUntil=Now.AddSeconds(60);
                row.LastAttemptAt=Now; row.NextAttemptAt=null; row.Attempts++; row.AttemptsInCycle++; return true;
            }, ct, claim: token);
            if (!claimed) continue;
            var mail = await db.NotificationOutbox.AsNoTracking().SingleAsync(x=>x.Id==id,ct);
            var mode = config["Mail:Mode"] ?? (string.IsNullOrWhiteSpace(config["Mail:Host"]) ? "Capture" : "Smtp");
            var timer=Stopwatch.StartNew();
            string status, outcome="Succeeded"; string? errorCode=null, summary=null;
            DateTime? next=null, sent=null, captured=null;
            try
            {
                if (!NotificationService.IsAddress(mail.Recipient))
                    throw new EmailSendException("RecipientInvalid","Email người nhận chưa hợp lệ. Cần kiểm tra trước khi thử lại.");
                if (mode.Equals("Capture",StringComparison.OrdinalIgnoreCase)) { status="Captured"; captured=Now; }
                else if (mode.Equals("Smtp",StringComparison.OrdinalIgnoreCase))
                {
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await sender.SendAsync(new(mail.Id,$"<g4-notification-{mail.Id}@g4.example.test>",
                        mail.From ?? config["Mail:From"] ?? "g4@example.test",mail.Recipient,mail.Subject,mail.Body,mail.HtmlBody),timeout.Token);
                    status="Sent"; sent=Now;
                }
                else throw new EmailSendException("ModeInvalid","Chế độ email chưa hợp lệ.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is SmtpException or OperationCanceledException or EmailSendException or System.IO.IOException or InvalidOperationException or ArgumentException)
            {
                var uncertain=ex is OperationCanceledException or System.IO.IOException || ex is SmtpException smtp && smtp.StatusCode==SmtpStatusCode.GeneralFailure;
                outcome=uncertain ? "Unknown" : "Failed";
                errorCode=ex is EmailSendException permanent ? permanent.Code : ex is OperationCanceledException ? "Timeout" : uncertain ? "ResultUnknown" : "SmtpRejected";
                summary=ex is EmailSendException known ? known.Message : uncertain ? "Chưa xác định máy chủ đã nhận thư; hệ thống sẽ thử lại trong giới hạn." : "Máy chủ chưa chấp nhận email. Hệ thống sẽ thử lại trong giới hạn.";
                next=ex is EmailSendException or ArgumentException ? null : EmailRetryRules.NextAttemptAt(mail.AttemptsInCycle,Now);
                status=next==null ? "Failed" : "Pending";
            }
            await MutateAsync(db,id,token,"Processing",row=>
            {
                row.Status=status; row.NextAttemptAt=next; row.SentAt=sent; row.CapturedAt=captured;
                row.LastErrorCode=errorCode; row.LastErrorSummary=summary; row.ProcessingToken=null; row.ProcessingUntil=null; return true;
            },ct);
            await LogAsync(mail,mode,outcome,timer.ElapsedMilliseconds,errorCode,summary,ct);
            handled++;
        }
        return handled;
    }

    public async Task<bool> RetryFailedAsync(int id,CancellationToken ct) => await MutateAsync(
        await factory.CreateDbContextAsync(ct),id,null,"Failed", row=>
        { row.Status="Pending"; row.Cycle++; row.AttemptsInCycle=0; row.NextAttemptAt=Now; row.ProcessingToken=null; row.ProcessingUntil=null; return true; },ct,dispose:true);

    private async Task RecoverAsync(ApplicationDbContext db,int id,CancellationToken ct)
    {
        var row=await db.NotificationOutbox.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
        if(row?.Status!="Processing" || row.ProcessingUntil>Now) return;
        var next=row.AttemptsInCycle is >=1 and <=3 ? EmailRetryRules.NextAttemptAt(row.AttemptsInCycle,row.LastAttemptAt ?? Now) : null;
        var changed=await MutateAsync(db,id,row.ProcessingToken,"Processing", x=>
        {
            if(x.ProcessingUntil>Now)return false;
            x.Status=next==null ? "Failed" : "Pending"; x.NextAttemptAt=next;
            x.ProcessingUntil=null; x.ProcessingToken=null; x.LastErrorCode="LeaseExpired";
            x.LastErrorSummary="Lần xử lý trước bị gián đoạn; chưa xác định kết quả gửi."; return true;
        },ct);
        if(changed) await LogAsync(row,"Recovery","Unknown",0,"LeaseExpired","Lần xử lý trước bị gián đoạn; chưa xác định kết quả gửi.",ct);
    }

    private async Task<bool> MutateAsync(ApplicationDbContext db,int id,Guid? expectedToken,string expectedStatus,
        Func<NotificationOutbox,bool> mutation,CancellationToken ct,Guid? claim=null,bool dispose=false)
    {
        try
        {
            if(!db.Database.IsRelational()) await InMemoryGate.WaitAsync(ct);
            try
            {
                var row=await db.NotificationOutbox.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
                if(row==null || row.Status!=expectedStatus || row.ProcessingToken!=expectedToken || !mutation(row))return false;
                if(db.Database.IsRelational())
                {
                    var query=db.NotificationOutbox.Where(x=>x.Id==id && x.Status==expectedStatus && x.ProcessingToken==expectedToken);
                    if(claim!=null) { var now=Now; query=query.Where(x=>(x.NextAttemptAt==null || x.NextAttemptAt<=now) && x.AttemptsInCycle<3); }
                    return await query.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,row.Status).SetProperty(x=>x.Attempts,row.Attempts)
                        .SetProperty(x=>x.AttemptsInCycle,row.AttemptsInCycle).SetProperty(x=>x.Cycle,row.Cycle)
                        .SetProperty(x=>x.NextAttemptAt,row.NextAttemptAt).SetProperty(x=>x.LastAttemptAt,row.LastAttemptAt)
                        .SetProperty(x=>x.SentAt,row.SentAt).SetProperty(x=>x.CapturedAt,row.CapturedAt)
                        .SetProperty(x=>x.ProcessingToken,row.ProcessingToken).SetProperty(x=>x.ProcessingUntil,row.ProcessingUntil)
                        .SetProperty(x=>x.LastErrorCode,row.LastErrorCode).SetProperty(x=>x.LastErrorSummary,row.LastErrorSummary),ct)==1;
                }
                db.ChangeTracker.Clear(); db.NotificationOutbox.Update(row); await db.SaveChangesAsync(ct); return true;
            }
            finally { if(!db.Database.IsRelational()) InMemoryGate.Release(); }
        }
        finally { if(dispose) await db.DisposeAsync(); }
    }

    private async Task LogAsync(NotificationOutbox mail,string mode,string outcome,long duration,string? code,string? summary,CancellationToken ct)
    {
        if(logs==null)return;
        try { await logs.WriteAsync(new(mail.OrderId,Guid.NewGuid().ToString("N"),"Email",mode=="Capture"?"Capture":"Send",mode,
            mail.Id.ToString(),mail.Attempts,outcome,null,duration,null,code,summary,mail.Id,mail.Cycle,Now),ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { /* Diagnostics never triggers another send. */ }
    }
}
