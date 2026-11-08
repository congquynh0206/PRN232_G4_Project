using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<ImmediateCheckout> ImmediateCheckouts => Set<ImmediateCheckout>();
    public DbSet<ImmediatePaymentAttempt> ImmediatePaymentAttempts => Set<ImmediatePaymentAttempt>();

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
            entity.HasIndex(x => x.Key).IsUnique();
            entity.HasOne<ImmediateCheckout>().WithMany().HasForeignKey(x => x.CheckoutId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<OrderTable>().WithMany().HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
