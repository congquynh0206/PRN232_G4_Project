using Microsoft.EntityFrameworkCore;

namespace backend.Models;

public partial class ApplicationDbContext
{
    public DbSet<ShippingEvent> ShippingEvents => Set<ShippingEvent>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<NotificationOutbox> NotificationOutbox => Set<NotificationOutbox>();

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderTable>(entity =>
        {
            entity.HasIndex(x => x.CheckoutKey).IsUnique().HasFilter("[CheckoutKey] IS NOT NULL");
            entity.Property(x => x.Subtotal).HasColumnType("decimal(10,2)");
            entity.Property(x => x.ShippingFee).HasColumnType("decimal(10,2)");
            entity.Property(x => x.DiscountAmount).HasColumnType("decimal(10,2)");
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.Property(x => x.CouponCode).HasMaxLength(50);
            entity.Property(x => x.CheckoutKey).HasMaxLength(100);
            entity.Property(x => x.CancelPreviousStatus).HasMaxLength(20);
            entity.Property(x => x.CancelDecisionReason).HasMaxLength(500);
        });
        modelBuilder.Entity<OrderItem>().Property(x => x.ProductTitleSnapshot).HasMaxLength(255);
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
            entity.HasIndex(x => x.ProviderTransactionId).IsUnique().HasFilter("[ProviderTransactionId] IS NOT NULL");
            entity.Property(x => x.ProviderOrderId).HasMaxLength(100);
            entity.Property(x => x.ProviderTransactionId).HasMaxLength(100);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(100);
            entity.Property(x => x.ErrorCode).HasMaxLength(100);
        });
        modelBuilder.Entity<ShippingInfo>(entity =>
        {
            entity.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
            entity.HasIndex(x => x.TrackingNumber).IsUnique().HasFilter("[trackingNumber] IS NOT NULL");
            entity.Property(x => x.Direction).HasMaxLength(20);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(100);
        });
        modelBuilder.Entity<ShippingEvent>(entity =>
        {
            entity.ToTable("ShippingEvent");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ExternalEventId).IsUnique();
            entity.Property(x => x.ExternalEventId).HasMaxLength(100);
            entity.Property(x => x.Status).HasMaxLength(50);
            entity.Property(x => x.Location).HasMaxLength(100);
            entity.HasOne<ShippingInfo>().WithMany().HasForeignKey(x => x.ShippingInfoId);
        });
        modelBuilder.Entity<Refund>(entity =>
        {
            entity.ToTable("Refund");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.Property(x => x.Amount).HasColumnType("decimal(10,2)");
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.Property(x => x.Reason).HasMaxLength(100);
            entity.Property(x => x.Status).HasMaxLength(20);
            entity.HasOne<OrderTable>().WithMany().HasForeignKey(x => x.OrderId);
            entity.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<NotificationOutbox>(entity =>
        {
            entity.ToTable("NotificationOutbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).HasMaxLength(50);
            entity.Property(x => x.Recipient).HasMaxLength(100);
            entity.Property(x => x.Status).HasMaxLength(20);
            entity.HasIndex(x => new { x.OrderId, x.EventType }).IsUnique();
        });
    }
}
