using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<ImmediateCheckout> ImmediateCheckouts => Set<ImmediateCheckout>();
    public DbSet<ImmediatePaymentAttempt> ImmediatePaymentAttempts => Set<ImmediatePaymentAttempt>();
    public DbSet<SimulatedCardAuthorization> SimulatedCardAuthorizations =>
        Set<SimulatedCardAuthorization>();

    private static void ConfigureImmediateCheckout(ModelBuilder model)
    {
        model.Entity<Product>().Property(x => x.ImmediatePaymentRequired).HasDefaultValue(false);
        model.Entity<Product>().Property(x => x.IsListed).HasDefaultValue(true);
        model.Entity<ImmediateCheckout>(entity =>
        {
            entity.ToTable("ImmediateCheckout");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(20);
            entity.HasIndex(x => new { x.BuyerId, x.CreatedAt });
            entity.HasIndex(x => x.OrderId).IsUnique().HasFilter("[OrderId] IS NOT NULL");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.BuyerId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<OrderTable>().WithMany().HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<ImmediatePaymentAttempt>(entity =>
        {
            entity.ToTable("ImmediatePaymentAttempt");
            entity.Property(x => x.Key).HasMaxLength(100);
            entity.Property(x => x.Status).HasMaxLength(20);
            entity.Property(x => x.ErrorCode).HasMaxLength(100);
            entity.Property(x => x.Stage).HasMaxLength(30);
            entity.Property(x => x.AuthorizationOutcome).HasMaxLength(30);
            entity.Property(x => x.AuthorizationId).HasMaxLength(100);
            entity.Property(x => x.CaptureId).HasMaxLength(100);
            entity.Property(x => x.Amount).HasPrecision(10, 2);
            entity.HasIndex(x => new { x.Status, x.ProcessingUntil });
            entity.HasIndex(x => x.Key).IsUnique();
            entity.HasOne<ImmediateCheckout>().WithMany().HasForeignKey(x => x.CheckoutId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<OrderTable>().WithMany().HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<SimulatedCardAuthorization>(entity =>
        {
            entity.ToTable("SimulatedCardAuthorization");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.Status).HasMaxLength(30);
            entity.Property(x => x.CaptureId).HasMaxLength(100);
            entity.Property(x => x.Amount).HasPrecision(10, 2);
            entity.Property(x => x.RefundedAmount).HasPrecision(10, 2);
        });
    }
}
