using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;
namespace G4.Infrastructure.Diagnostics;
public sealed class IntegrationLogWriter(IDbContextFactory<ApplicationDbContext> factory,ILogger<IntegrationLogWriter> logger):IIntegrationLogWriter
{
    public async Task WriteAsync(IntegrationLogEntry entry,CancellationToken ct)
    {
        try
        {
            using var suppress=new TransactionScope(TransactionScopeOption.Suppress,TransactionScopeAsyncFlowOption.Enabled);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var db=await factory.CreateDbContextAsync(timeout.Token);
            if(db.Database.IsRelational())db.Database.SetCommandTimeout(2);
            db.IntegrationLogs.Add(new IntegrationLog {
                OrderId=entry.OrderId,CorrelationId=entry.CorrelationId,Service=entry.Service,Operation=entry.Operation,Mode=entry.Mode,
                EntityId=entry.EntityId,Attempt=entry.Attempt,Outcome=entry.Outcome,HttpStatus=entry.HttpStatus,DurationMs=Math.Max(0,entry.DurationMs),
                ProviderReference=entry.ProviderReference,ErrorCode=entry.ErrorCode,ErrorSummary=entry.ErrorSummary,
                NotificationId=entry.NotificationId,Cycle=entry.Cycle,CreatedAt=entry.CreatedAt });
            await db.SaveChangesAsync(timeout.Token); suppress.Complete();
        }
        catch(Exception) { logger.LogWarning("Could not persist integration diagnostic for service {Service}, operation {Operation}",entry.Service,entry.Operation); }
    }
}
