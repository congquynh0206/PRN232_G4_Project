using Microsoft.EntityFrameworkCore;
namespace G4.Infrastructure.Persistence;
public partial class ApplicationDbContext
{
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionTarget> PromotionTargets => Set<PromotionTarget>();
    public DbSet<PromotionTier> PromotionTiers => Set<PromotionTier>();
    public DbSet<PromotionUsage> PromotionUsages => Set<PromotionUsage>();
    public DbSet<OrderPromotionSnapshot> OrderPromotionSnapshots => Set<OrderPromotionSnapshot>();
    public DbSet<PromotionAudit> PromotionAudits => Set<PromotionAudit>();
    private static void ConfigurePromotions(ModelBuilder model)
    {
        model.Entity<Promotion>(e =>
        {
            e.ToTable("Promotion");
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Type).HasMaxLength(20);
            e.Property(x => x.FundingSource).HasMaxLength(20);
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.PauseReason).HasMaxLength(1000);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Code).IsUnique().HasFilter("[Code] IS NOT NULL");
            e.HasIndex(x => x.LegacyCouponId).IsUnique().HasFilter("[LegacyCouponId] IS NOT NULL");
            e.HasIndex(x => new { x.SellerId, x.Type, x.StartAt, x.EndAt });
            e.HasMany(x => x.Targets).WithOne().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Tiers).WithOne().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<PromotionTarget>(e =>
        {
            e.ToTable("PromotionTarget");
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<PromotionTier>(e => { e.ToTable("PromotionTier"); e.HasIndex(x => new { x.PromotionId, x.MinQuantity }).IsUnique(); });
        model.Entity<PromotionUsage>(e =>
        {
            e.ToTable("PromotionUsage");
            e.Property(x => x.State).HasMaxLength(20);
            e.HasIndex(x => new { x.OrderId, x.PromotionId }).IsUnique();
            e.HasIndex(x => new { x.PromotionId, x.State, x.BuyerId });
            e.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<OrderTable>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.BuyerId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<OrderPromotionSnapshot>(e =>
        {
            e.ToTable("OrderPromotionSnapshot");
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Type).HasMaxLength(20);
            e.Property(x => x.FundingSource).HasMaxLength(20);
            e.Property(x => x.Code).HasMaxLength(50);
            e.HasIndex(x => new { x.OrderId, x.PromotionId }).IsUnique();
            e.HasOne<OrderTable>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<PromotionAudit>(e =>
        {
            e.ToTable("PromotionAudit"); e.Property(x => x.Action).HasMaxLength(30); e.Property(x => x.Reason).HasMaxLength(1000);
            e.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
        });
        foreach (var type in new[] { typeof(Promotion), typeof(PromotionTier), typeof(PromotionUsage), typeof(OrderPromotionSnapshot) })
            foreach (var p in model.Model.FindEntityType(type)!.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
                p.SetColumnType("decimal(18,2)");
        foreach (var name in new[] { nameof(OrderTable.ShippingBase), nameof(OrderTable.ShippingDiscount), nameof(OrderTable.SellerGoodsDiscount), nameof(OrderTable.PlatformSubsidy), nameof(OrderTable.SellerGrossSnapshot) })
            model.Entity<OrderTable>().Property<decimal>(name).HasColumnType("decimal(18,2)");
        model.Entity<OrderTable>().Property(x => x.PricingFingerprint).HasMaxLength(64);
        model.Entity<OrderItem>().Property(x => x.SellerDiscountSnapshot).HasColumnType("decimal(18,2)");
        model.Entity<OrderItem>().Property(x => x.PlatformDiscountSnapshot).HasColumnType("decimal(18,2)");
    }
}
