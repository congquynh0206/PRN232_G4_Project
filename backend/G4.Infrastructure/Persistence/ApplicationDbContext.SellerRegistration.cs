using Microsoft.EntityFrameworkCore;
namespace G4.Infrastructure.Persistence;
public partial class ApplicationDbContext
{
    public DbSet<SellerIdentity> SellerIdentities => Set<SellerIdentity>();
    public DbSet<SellerIdentityKey> SellerIdentityKeys => Set<SellerIdentityKey>();
    public DbSet<SellerRegistration> SellerRegistrations => Set<SellerRegistration>();
    public DbSet<SellerBlacklistEntry> SellerBlacklist => Set<SellerBlacklistEntry>();
    public DbSet<SellerDebtWriteOff> SellerDebtWriteOffs => Set<SellerDebtWriteOff>();
    public DbSet<SellerWriteOffRecovery> SellerWriteOffRecoveries => Set<SellerWriteOffRecovery>();

    private static void ConfigureSellerRegistration(ModelBuilder model)
    {
        model.Entity<SellerAccount>().HasOne<SellerIdentity>().WithMany().HasForeignKey(x => x.CurrentIdentityId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<SellerDebtCycle>().Property(x => x.LastObservedBalance).HasPrecision(18, 2);
        model.Entity<SellerIdentityKey>(e =>
        {
            e.ToTable("SellerIdentityKey"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(40); e.Property(x => x.Verifier).HasMaxLength(64);
        });
        model.Entity<SellerIdentity>(e =>
        {
            e.ToTable("SellerIdentity");
            e.Property(x => x.Kind).HasMaxLength(20); e.Property(x => x.MaskedValue).HasMaxLength(100);
            e.Property(x => x.KeyId).HasMaxLength(40); e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.Property(x => x.RequestKey).HasMaxLength(100); e.Property(x => x.RequestFingerprint).HasMaxLength(64);
            e.HasIndex(x => new { x.KeyId, x.Fingerprint }).IsUnique();
            e.HasIndex(x => new { x.SellerId, x.RequestKey }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<SellerRegistration>(e =>
        {
            e.ToTable("SellerRegistration");
            e.Property(x => x.RequestKey).HasMaxLength(100); e.Property(x => x.RequestFingerprint).HasMaxLength(64);
            e.Property(x => x.Email).HasMaxLength(100); e.Property(x => x.StoreName).HasMaxLength(100);
            e.HasIndex(x => x.RequestKey).IsUnique(); e.HasIndex(x => x.SellerId).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<SellerDebtWriteOff>(e =>
        {
            e.ToTable("SellerDebtWriteOff"); e.Property(x => x.OriginalAmount).HasPrecision(18, 2);
            e.Property(x => x.Actor).HasMaxLength(40); e.Property(x => x.Reason).HasMaxLength(1000);
            e.HasIndex(x => new { x.SellerAccountId, x.CycleNumber }).IsUnique();
            e.HasOne<SellerAccount>().WithMany().HasForeignKey(x => x.SellerAccountId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<SellerWriteOffRecovery>(e =>
        {
            e.ToTable("SellerWriteOffRecovery"); e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne<SellerDebtWriteOff>().WithMany().HasForeignKey(x => x.WriteOffId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<SellerBlacklistEntry>(e =>
        {
            e.ToTable("SellerBlacklist"); e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.MaskedValue).HasMaxLength(100); e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.Property(x => x.KeyId).HasMaxLength(40); e.Property(x => x.Reason).HasMaxLength(1000);
            e.HasIndex(x => new { x.KeyId, x.Fingerprint }).IsUnique();
            e.HasOne<SellerDebtWriteOff>().WithMany().HasForeignKey(x => x.WriteOffId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SourceSellerId).OnDelete(DeleteBehavior.NoAction);
        });
    }
    private void GuardSellerRegistrationHistory()
    {
        if (ChangeTracker.Entries().Any(e => (e.State is EntityState.Modified or EntityState.Deleted) &&
            e.Entity is SellerIdentity or SellerIdentityKey or SellerRegistration or SellerDebtWriteOff or SellerWriteOffRecovery or SellerBlacklistEntry))
            throw new InvalidOperationException("Lịch sử định danh, blacklist và quyết định nợ khó thu không thể sửa hoặc xóa.");
    }
}
