using G4.Application.Integrations;
using G4.Infrastructure.Diagnostics;
using G4.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

public static class IntegrationBufferChecks
{
    public static async Task RunAsync()
    {
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var factory=new BlockingFactory(options);
        var services=new ServiceCollection();
        services.AddScoped(_=>new IntegrationLogWriter(factory,NullLogger<IntegrationLogWriter>.Instance));
        using var provider=services.BuildServiceProvider();
        using var buffer=new BufferedIntegrationLogWriter(provider.GetRequiredService<IServiceScopeFactory>(),NullLogger<BufferedIntegrationLogWriter>.Instance);
        await buffer.StartAsync(default);
        var entry=new IntegrationLogEntry(17,"buffer-test","PayPal","Refund","Sandbox",null,1,"Succeeded",200,1,"RF-17",null,null,null,null,DateTime.UtcNow);
        await buffer.WriteAsync(entry,default);
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        // SQL is blocked; logging a second actual call must complete synchronously.
        if(!buffer.WriteAsync(entry with {Operation="OAuth"},default).IsCompletedSuccessfully)
            throw new Exception("Log queue consumes business cancellation budget");
        for(var i=0;i<600;i++)if(!buffer.WriteAsync(entry,default).IsCompletedSuccessfully)throw new Exception("Full diagnostic queue blocked a business call");
        factory.Release.TrySetResult();
        using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await buffer.StopAsync(stop.Token);
        await using var db=new ApplicationDbContext(options);
        if(await db.IntegrationLogs.CountAsync()!=513)throw new Exception("Log queue did not drain bounded entries at shutdown");
        Console.WriteLine("Bounded diagnostic queue nonblocking SQL/saturation and graceful drain passed");
    }
    private sealed class BlockingFactory(DbContextOptions<ApplicationDbContext> options):IDbContextFactory<ApplicationDbContext>
    {
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ApplicationDbContext CreateDbContext()=>new(options);
        public async Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken ct=default)
        {Entered.TrySetResult();await Release.Task.WaitAsync(ct);return CreateDbContext();}
    }
}
