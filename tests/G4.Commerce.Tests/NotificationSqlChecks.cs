using G4.Application.Integrations;
using G4.Domain.Entities;
using G4.Infrastructure.Persistence;
using G4.Infrastructure.Notifications;
using G4.Infrastructure.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.SqlClient;

public static class NotificationSqlChecks
{
    public static async Task RunAsync()
    {
        var connection=Environment.GetEnvironmentVariable("G4_EMAIL_SQL_TEST_CONNECTION") ?? throw new Exception("QA connection required");
        if(new SqlConnectionStringBuilder(connection).InitialCatalog!="G4_Email_Test_20261005")throw new Exception("SQL checks are restricted to the dedicated QA database");
        var factory=new NotificationProcessorBehaviorChecks.Factory(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Mail:Mode"]="Smtp" }).Build();
        var writer=new IntegrationLogWriter(factory,NullLogger<IntegrationLogWriter>.Instance);
        // Re-run only replaces this test's orphan fixtures, never real QA orders or the legacy migration fixture.
        var fixtureOrders=new[] {100010,100011,100012,100099};
        await using(var reset=factory.CreateDbContext())
        {
            if(await reset.OrderTables.AnyAsync(x=>fixtureOrders.Contains(x.Id)))throw new Exception("SQL fixture IDs overlap actual orders");
            await reset.NotificationOutbox.Where(x=>fixtureOrders.Contains(x.OrderId)).ExecuteDeleteAsync();
            await reset.IntegrationLogs.Where(x=>x.OrderId!=null&&fixtureOrders.Contains(x.OrderId.Value)).ExecuteDeleteAsync();
        }
        await using(var txdb=factory.CreateDbContext())
        {
            await using var tx=await txdb.Database.BeginTransactionAsync();
            var rollbackMail=new NotificationOutbox { OrderId=100099,EventType="Delivered",Recipient="buyer@example.test",Status="Pending",CreatedAt=DateTime.UtcNow };
            txdb.NotificationOutbox.Add(rollbackMail);await txdb.SaveChangesAsync();
            await writer.WriteAsync(new(100099,"sql-rollback-test","Email","Capture","Capture",null,1,"Succeeded",null,0,null,null,null,rollbackMail.Id,1,DateTime.UtcNow),default);
            await tx.RollbackAsync();
            await using var inspect=factory.CreateDbContext();
            if(await inspect.NotificationOutbox.AnyAsync(x=>x.Id==rollbackMail.Id)||!await inspect.IntegrationLogs.AnyAsync(x=>x.CorrelationId=="sql-rollback-test"))throw new Exception("Outbox rollback or independent log writer failed");
        }
        int id;
        await using(var db=factory.CreateDbContext())
        {
            var row=new NotificationOutbox { OrderId=100010,EventType="Delivered",Recipient="buyer@example.test",From="g4@example.test",CreatedAt=DateTime.UtcNow };
            db.Add(row);await db.SaveChangesAsync();id=row.Id;
            db.Add(new NotificationOutbox { OrderId=100010,EventType="Delivered" });
            try { await db.SaveChangesAsync();throw new Exception("SQL unique event index missing"); } catch(DbUpdateException) { }
        }
        var blocked=new BlockingSender();
        var first=new NotificationProcessor(factory,blocked,config,TimeProvider.System,writer);
        var second=new NotificationProcessor(factory,blocked,config,TimeProvider.System,writer);
        var firstRun=first.ProcessDueAsync(100,default);
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await second.ProcessDueAsync(100,default);
        blocked.Release.TrySetResult();await firstRun;
        await using(var db=factory.CreateDbContext())
        {
            var row=await db.NotificationOutbox.SingleAsync(x=>x.Id==id);
            if(blocked.Calls!=1 || row.Status!="Sent" || row.Attempts!=1)throw new Exception("Two SQL processors sent the same email");
            row.Status="Failed";await db.SaveChangesAsync();
        }
        var retries=await Task.WhenAll(first.RetryFailedAsync(id,default),second.RetryFailedAsync(id,default));
        if(retries.Count(x=>x)!=1)throw new Exception("Concurrent SQL retry accepted more than once");
        await using(var db=factory.CreateDbContext())
        {
            var row=await db.NotificationOutbox.SingleAsync(x=>x.Id==id);
            row.Status="Failed"; await db.SaveChangesAsync();
            db.Add(new NotificationOutbox { OrderId=100011,EventType="Delivered",Status="Processing",Attempts=3,AttemptsInCycle=3,
                ProcessingToken=Guid.NewGuid(),ProcessingUntil=DateTime.UtcNow.AddSeconds(-1),LastAttemptAt=DateTime.UtcNow.AddMinutes(-5) });
            await db.SaveChangesAsync();
        }
        await second.ProcessDueAsync(100,default);
        await using(var db=factory.CreateDbContext())
        { if((await db.NotificationOutbox.SingleAsync(x=>x.OrderId==100011)).Status!="Failed")throw new Exception("Lease recovery exceeded cycle limit"); }
        int staleId;var stale=new BlockingSender();
        await using(var db=factory.CreateDbContext())
        { var row=new NotificationOutbox { OrderId=100012,EventType="Delivered",Recipient="buyer@example.test",From="g4@example.test",CreatedAt=DateTime.UtcNow };db.Add(row);await db.SaveChangesAsync();staleId=row.Id; }
        var oldRun=new NotificationProcessor(factory,stale,config,TimeProvider.System,writer).ProcessDueAsync(100,default);
        await stale.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));var replacement=Guid.NewGuid();
        await using(var db=factory.CreateDbContext())
        { await db.NotificationOutbox.Where(x=>x.Id==staleId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ProcessingToken,replacement).SetProperty(x=>x.ProcessingUntil,DateTime.UtcNow.AddMinutes(1))); }
        stale.Release.TrySetResult();await oldRun;
        await using(var db=factory.CreateDbContext())
        {
            var row=await db.NotificationOutbox.SingleAsync(x=>x.Id==staleId);
            if(row.Status!="Processing" || row.ProcessingToken!=replacement || row.SentAt!=null)throw new Exception("Old lease owner overwrote new owner");
            row.Status="Failed";row.ProcessingToken=null;row.ProcessingUntil=null;await db.SaveChangesAsync();
            var page=await new DiagnosticsQueryService(db).LogsAsync(1,"admin",new(),default);
            if(page.TotalCount<4)throw new Exception("SQL log projection/history missing");
        }
        Console.WriteLine("Real SQL uniqueness, rollback/log isolation, concurrent claim/retry, lease limit/token and projection passed");
    }
    private sealed class BlockingSender:IEmailSender
    {
        public int Calls;
        public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task SendAsync(EmailEnvelope mail,CancellationToken ct) { Interlocked.Increment(ref Calls);Started.TrySetResult();await Release.Task.WaitAsync(ct); }
    }
}
