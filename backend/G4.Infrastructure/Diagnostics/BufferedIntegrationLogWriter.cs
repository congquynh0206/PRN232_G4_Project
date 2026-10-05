using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace G4.Infrastructure.Diagnostics;

// Best-effort diagnostics must never consume the payment/refund cancellation budget.
public sealed class BufferedIntegrationLogWriter(IServiceScopeFactory scopes, ILogger<BufferedIntegrationLogWriter> logger)
    : BackgroundService, IIntegrationLogWriter
{
    private readonly Channel<IntegrationLogEntry> queue = Channel.CreateBounded<IntegrationLogEntry>(
        new BoundedChannelOptions(512) { SingleReader=true, FullMode=BoundedChannelFullMode.Wait });

    public Task WriteAsync(IntegrationLogEntry entry, CancellationToken ct)
    {
        if (!queue.Writer.TryWrite(entry))
            logger.LogWarning("Integration diagnostic queue is full or stopping; entry was discarded.");
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var entry in queue.Reader.ReadAllAsync(stoppingToken))
            {
                using var scope=scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IntegrationLogWriter>().WriteAsync(entry, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Writer.TryComplete();
        try { if (ExecuteTask!=null) await ExecuteTask.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        await base.StopAsync(cancellationToken);
    }
}
