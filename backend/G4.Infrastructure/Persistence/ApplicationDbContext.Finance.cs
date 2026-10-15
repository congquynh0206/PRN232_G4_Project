using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<PlatformFeePolicy> PlatformFeePolicies => Set<PlatformFeePolicy>();

    private static void ConfigureFinancePolicies(ModelBuilder model)
    {
        model.Entity<PlatformFeePolicy>(e =>
        {
            e.ToTable("PlatformFeePolicy");
            e.Property(x => x.FeePercent).HasPrecision(5, 2);
            e.Property(x => x.FixedFee).HasPrecision(18, 2);
            e.Property(x => x.PreviousFeePercent).HasPrecision(5, 2);
            e.Property(x => x.PreviousFixedFee).HasPrecision(18, 2);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.RequestKey).HasMaxLength(100);
            e.HasIndex(x => x.RequestKey).IsUnique();
            e.HasIndex(x => x.EffectiveAt).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ChangedById).OnDelete(DeleteBehavior.NoAction);
            e.HasData(new PlatformFeePolicy
            {
                Id = 1, FeePercent = 12m, FixedFee = .30m, PreviousFeePercent = 12m, PreviousFixedFee = .30m,
                EffectiveAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Reason = "Mức phí mặc định ban đầu", RequestKey = "initial-platform-fee"
            });
        });
        model.Entity<SellerSettlement>(e =>
        {
            e.Property(x => x.FeePercentSnapshot).HasPrecision(5, 2);
            e.HasOne<PlatformFeePolicy>().WithMany().HasForeignKey(x => x.FeePolicyId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private void GuardFinanceHistory()
    {
        if (ChangeTracker.Entries<SellerDebtRepayment>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<SellerDebtEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Biên nhận và lịch sử nợ không thể sửa hoặc xóa.");
        if (ChangeTracker.Entries<PlatformFeePolicy>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Lịch sử cấu hình phí không thể sửa hoặc xóa. Hãy tạo mức phí mới.");
    }
}
