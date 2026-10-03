using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<ShippingEvent> ShippingEvents => Set<ShippingEvent>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<NotificationOutbox> NotificationOutbox => Set<NotificationOutbox>();
    public DbSet<SellerAccount> SellerAccounts => Set<SellerAccount>();
    public DbSet<SellerSettlement> SellerSettlements => Set<SellerSettlement>();
    public DbSet<FinancialTransaction> FinancialTransactions => Set<FinancialTransaction>();
    public DbSet<SellerPayout> SellerPayouts => Set<SellerPayout>();

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Dispute>(entity =>
        {
            entity.Property(x => x.Proposal).HasMaxLength(30);
            entity.Property(x => x.Outcome).HasMaxLength(30);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.OrderId).IsUnique()
                .HasFilter("[IsOpen] = 1 AND [WorkflowEnabled] = 1 AND [orderId] IS NOT NULL");
            entity.HasIndex(x => new { x.WorkflowEnabled, x.Status, x.SellerResponseDueAt });
        });
        modelBuilder.Entity<DisputeEntry>(entity =>
        {
            entity.ToTable("DisputeEntry");
            entity.Property(x => x.ActorRole).HasMaxLength(20);
            entity.Property(x => x.Kind).HasMaxLength(30);
            entity.Property(x => x.Description).HasMaxLength(4000);
            entity.HasIndex(x => new { x.DisputeId, x.Id });
            entity.HasOne<Dispute>().WithMany().HasForeignKey(x => x.DisputeId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
        });
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
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
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
        modelBuilder.Entity<SellerAccount>(entity =>
        {
            entity.ToTable("SellerAccount");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.SellerId).IsUnique();
            entity.Property(x => x.Status).HasMaxLength(20);
            entity.Property(x => x.MonthlySalesLimit).HasColumnType("decimal(18,2)");
            entity.Property(x => x.ProcessingBalance).HasColumnType("decimal(18,2)");
            entity.Property(x => x.AvailableBalance).HasColumnType("decimal(18,2)");
            entity.Property(x => x.OnHoldBalance).HasColumnType("decimal(18,2)");
            entity.Property(x => x.NegativeBalance).HasColumnType("decimal(18,2)");
            entity.Property(x => x.MonthlySalesAmount).HasColumnType("decimal(18,2)");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<SellerSettlement>(entity =>
        {
            entity.ToTable("SellerSettlement");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OrderId).IsUnique();
            entity.HasIndex(x => x.PaymentId).IsUnique();
            entity.Property(x => x.GrossAmount).HasColumnType("decimal(18,2)");
            entity.Property(x => x.PlatformFeeAmount).HasColumnType("decimal(18,2)");
            entity.Property(x => x.FixedFeeAmount).HasColumnType("decimal(18,2)");
            entity.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
            entity.Property(x => x.ProcessingAmount).HasColumnType("decimal(18,2)");
            entity.Property(x => x.RefundedAmount).HasColumnType("decimal(18,2)");
            entity.Property(x => x.FeeCreditAmount).HasColumnType("decimal(18,2)");
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.Property(x => x.Status).HasMaxLength(30);
            entity.HasOne<SellerAccount>().WithMany().HasForeignKey(x => x.SellerAccountId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<OrderTable>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<SellerPayout>(entity =>
        {
            entity.ToTable("SellerPayout");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.Property(x => x.Status).HasMaxLength(20);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(100);
            entity.Property(x => x.DestinationMasked).HasMaxLength(30);
            entity.Property(x => x.BankReferenceId).HasMaxLength(100);
            entity.HasOne<SellerAccount>().WithMany().HasForeignKey(x => x.SellerAccountId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<FinancialTransaction>(entity =>
        {
            entity.ToTable("FinancialTransaction");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.EntryKey).IsUnique();
            entity.HasIndex(x => new { x.SellerAccountId, x.CreatedAt });
            entity.Property(x => x.Type).HasMaxLength(30);
            entity.Property(x => x.Bucket).HasMaxLength(20);
            entity.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.Property(x => x.EntryKey).HasMaxLength(100);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasOne<SellerAccount>().WithMany().HasForeignKey(x => x.SellerAccountId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<OrderTable>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<SellerSettlement>().WithMany().HasForeignKey(x => x.SettlementId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<SellerPayout>().WithMany().HasForeignKey(x => x.PayoutId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Refund>().WithMany().HasForeignKey(x => x.RefundId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
