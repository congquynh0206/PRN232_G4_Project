using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<DisputeDecision> DisputeDecisions => Set<DisputeDecision>();

    private static void ConfigureDisputeDecisions(ModelBuilder model)
    {
        model.Entity<DisputeDecision>(e =>
        {
            e.ToTable("DisputeDecision");
            e.HasIndex(x => x.DisputeId).IsUnique();
            e.HasIndex(x => x.RequestKey).IsUnique();
            e.Property(x => x.RequestKey).HasMaxLength(100);
            e.Property(x => x.PricingFingerprint).HasMaxLength(64);
            e.Property(x => x.Outcome).HasMaxLength(30);
            e.Property(x => x.Responsibility).HasMaxLength(20);
            e.Property(x => x.ShippingPayer).HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(4000);
            e.Property(x => x.EvidenceEntryIdsJson).HasMaxLength(2000);
            e.Property(x => x.Currency).HasMaxLength(3);
            foreach (
                var property in typeof(DisputeDecision)
                    .GetProperties()
                    .Where(p =>
                        p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?)
                    )
            )
                e.Property(property.Name).HasPrecision(18, 2);
            e.HasOne<Dispute>()
                .WithMany()
                .HasForeignKey(x => x.DisputeId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne<OrderTable>()
                .WithMany()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.AdminId)
                .OnDelete(DeleteBehavior.NoAction);
        });
        model
            .Entity<Refund>()
            .HasOne<DisputeDecision>()
            .WithMany()
            .HasForeignKey(x => x.DisputeDecisionId)
            .OnDelete(DeleteBehavior.NoAction);
    }

    private void GuardDisputeDecisions()
    {
        if (
            ChangeTracker
                .Entries<DisputeDecision>()
                .Any(e => e.State is EntityState.Modified or EntityState.Deleted)
        )
            throw new InvalidOperationException("Quyết định admin đã lưu không thể sửa hoặc xóa.");
    }
}
