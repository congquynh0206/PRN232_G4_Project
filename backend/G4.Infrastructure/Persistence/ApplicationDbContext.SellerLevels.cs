using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;
public partial class ApplicationDbContext
{
    public DbSet<SellerOrderFeedback> SellerOrderFeedbacks => Set<SellerOrderFeedback>();
    public DbSet<SellerLevelEvaluation> SellerLevelEvaluations => Set<SellerLevelEvaluation>();

    private static void ConfigureSellerLevels(ModelBuilder model)
    {
        model.Entity<SellerOrderFeedback>(e =>
        {
            e.ToTable("SellerOrderFeedback", t => t.HasCheckConstraint("CK_SellerOrderFeedback_Rating", "[Rating] >= 1 AND [Rating] <= 5"));
            e.HasIndex(x => x.OrderId).IsUnique();
            e.HasIndex(x => new { x.BuyerId, x.RequestKey }).IsUnique();
            e.HasIndex(x => new { x.SellerId, x.CreatedAt });
            e.Property(x => x.Comment).HasMaxLength(2000); e.Property(x => x.RequestKey).HasMaxLength(100);
            e.HasOne<OrderTable>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.BuyerId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<SellerLevelEvaluation>(e =>
        {
            e.ToTable("SellerLevelEvaluation");
            e.HasIndex(x => new { x.SellerId, x.Id });
            e.Property(x => x.Source).HasMaxLength(30); e.Property(x => x.Reason).HasMaxLength(1000);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<SellerAccount>().HasIndex(x => x.LastLevelEvaluatedAt);
    }

    private void GuardSellerLevelHistory()
    {
        if (ChangeTracker.Entries<SellerOrderFeedback>().Any(x => x.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<SellerLevelEvaluation>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Đánh giá đơn mua và lịch sử cấp seller không được sửa hoặc xóa.");
    }
}
