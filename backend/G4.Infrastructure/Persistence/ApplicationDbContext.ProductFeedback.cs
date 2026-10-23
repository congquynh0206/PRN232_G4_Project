using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<ProductReviewReply> ProductReviewReplies => Set<ProductReviewReply>();

    private static void ConfigureProductFeedback(ModelBuilder model)
    {
        model.Entity<Review>(e =>
        {
            e.Property(x => x.RequestKey).HasMaxLength(100);
            e.HasIndex(x => new { x.OrderId, x.ProductId })
                .IsUnique()
                .HasFilter("[OrderId] IS NOT NULL");
            e.HasIndex(x => new { x.ReviewerId, x.RequestKey })
                .IsUnique()
                .HasFilter("[RequestKey] IS NOT NULL");
            e.HasIndex(x => new { x.ProductId, x.CreatedAt });
            e.HasOne<OrderTable>()
                .WithMany()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<ProductReviewReply>(e =>
        {
            e.ToTable("ProductReviewReply");
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.Property(x => x.RequestKey).HasMaxLength(100);
            e.HasIndex(x => x.ReviewId).IsUnique();
            e.HasIndex(x => new { x.SellerId, x.RequestKey }).IsUnique();
            e.HasOne<Review>()
                .WithMany()
                .HasForeignKey(x => x.ReviewId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.SellerId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }

    private void GuardProductFeedback()
    {
        if (
            ChangeTracker
                .Entries<ProductReviewReply>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker
                .Entries<Review>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted)
        )
            throw new InvalidOperationException(
                "Đánh giá sản phẩm và phản hồi đã gửi không được sửa hoặc xóa."
            );
    }
}
