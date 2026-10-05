using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<IntegrationLog> IntegrationLogs => Set<IntegrationLog>();

    private static void ConfigureDiagnostics(ModelBuilder model)
    {
        model.Entity<NotificationOutbox>(e =>
        {
            e.Property(x => x.From).HasMaxLength(254);
            e.Property(x => x.Cycle).HasDefaultValue(1);
            e.Property(x => x.LastErrorCode).HasMaxLength(64);
            e.Property(x => x.LastErrorSummary).HasMaxLength(300);
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => new { x.Status, x.ProcessingUntil });
        });
        model.Entity<IntegrationLog>(e =>
        {
            e.ToTable("IntegrationLog");
            e.HasKey(x => x.Id);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.Property(x => x.Service).HasMaxLength(32);
            e.Property(x => x.Operation).HasMaxLength(64);
            e.Property(x => x.Mode).HasMaxLength(20);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.Outcome).HasMaxLength(20);
            e.Property(x => x.ProviderReference).HasMaxLength(200);
            e.Property(x => x.ErrorCode).HasMaxLength(64);
            e.Property(x => x.ErrorSummary).HasMaxLength(300);
            e.HasIndex(x => new { x.OrderId, x.CreatedAt, x.Id });
            e.HasIndex(x => new { x.Service, x.CreatedAt, x.Id });
            e.HasIndex(x => new { x.NotificationId, x.CreatedAt, x.Id });
        });
    }
}
