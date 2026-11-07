using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<UnpaidItemStrike> UnpaidItemStrikes => Set<UnpaidItemStrike>();

    private static void ConfigureUnpaidStrikes(ModelBuilder model)
    {
        model.Entity<User>(e =>
        {
            e.Property(x => x.BannedAt).HasColumnType("datetime2");
            e.Property(x => x.BanReason).HasMaxLength(500);
        });
        model.Entity<OrderTable>().Property(x => x.CancelReasonCode).HasMaxLength(20);
        model.Entity<UnpaidItemStrike>(e =>
        {
            e.ToTable("UnpaidItemStrike");
            e.HasKey(x => x.Id);
            e.Property(x => x.Reason).IsRequired().HasMaxLength(500);
            e.Property(x => x.CreatedAt).HasColumnType("datetime2");
            e.HasIndex(x => x.OrderId).IsUnique();
            e.HasIndex(x => new { x.BuyerId, x.CreatedAt });
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.BuyerId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne<OrderTable>()
                .WithMany()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
