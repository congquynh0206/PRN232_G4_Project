using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<PayPalCheckout> PayPalCheckouts => Set<PayPalCheckout>();

    private static void ConfigurePayPalCart(ModelBuilder model)
    {
        model.Entity<PayPalCheckout>(e =>
        {
            e.ToTable("PayPalCheckout");
            e.Property(x => x.CheckoutKey).HasMaxLength(80);
            e.Property(x => x.RequestKey).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(30);
            e.Property(x => x.ProviderOrderId).HasMaxLength(100);
            e.Property(x => x.ApprovalUrl).HasMaxLength(2000);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.HasIndex(x => new { x.BuyerId, x.RequestKey }).IsUnique();
            e.HasIndex(x => x.ProviderOrderId)
                .IsUnique()
                .HasFilter("[ProviderOrderId] IS NOT NULL");
            e.HasIndex(x => new { x.CheckoutKey, x.Status });
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.BuyerId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CheckoutBatch>()
                .WithMany()
                .HasForeignKey(x => x.CheckoutKey)
                .OnDelete(DeleteBehavior.NoAction);
        });
        model
            .Entity<Payment>()
            .HasOne<PayPalCheckout>()
            .WithMany()
            .HasForeignKey(x => x.PayPalCheckoutId)
            .OnDelete(DeleteBehavior.NoAction);
        model.Entity<Payment>().Property(x => x.ReservedSellerGross).HasPrecision(18, 2);
        model
            .Entity<Payment>()
            .HasIndex(x => new { x.PayPalCheckoutId, x.OrderId })
            .IsUnique()
            .HasFilter("[PayPalCheckoutId] IS NOT NULL");
    }
}
