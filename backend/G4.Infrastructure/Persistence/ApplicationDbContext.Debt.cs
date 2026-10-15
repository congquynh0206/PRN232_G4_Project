using Microsoft.EntityFrameworkCore;
namespace G4.Infrastructure.Persistence;
public partial class ApplicationDbContext
{
    public DbSet<SellerDebtCycle> SellerDebtCycles => Set<SellerDebtCycle>();
    public DbSet<SellerDebtRepayment> SellerDebtRepayments => Set<SellerDebtRepayment>();
    public DbSet<SellerDebtEvent> SellerDebtEvents => Set<SellerDebtEvent>();

    private static void ConfigureSellerDebt(ModelBuilder model)
    {
        model.Entity<SellerAccount>().HasIndex(x => new { x.NegativeBalance, x.DebtDueAt });
        model.Entity<SellerDebtCycle>(e =>
        {
            e.ToTable("SellerDebtCycle");
            e.Property(x => x.OpeningBalance).HasPrecision(18, 2);
            e.HasIndex(x => new { x.SellerAccountId, x.Number }).IsUnique();
            e.HasOne<SellerAccount>().WithMany().HasForeignKey(x => x.SellerAccountId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<SellerDebtRepayment>(e =>
        {
            e.ToTable("SellerDebtRepayment");
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.ExpectedDebt).HasPrecision(18, 2);
            e.Property(x => x.RemainingDebt).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.RequestKey).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.BankReferenceId).HasMaxLength(100);
            e.HasIndex(x => new { x.SellerAccountId, x.RequestKey }).IsUnique();
            e.HasOne<SellerAccount>().WithMany().HasForeignKey(x => x.SellerAccountId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<SellerDebtEvent>(e =>
        {
            e.ToTable("SellerDebtEvent");
            e.Property(x => x.Balance).HasPrecision(18, 2);
            e.Property(x => x.EventType).HasMaxLength(40);
            e.Property(x => x.EntryKey).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.HasIndex(x => x.EntryKey).IsUnique();
            e.HasIndex(x => new { x.SellerAccountId, x.Id });
            e.HasOne<SellerAccount>().WithMany().HasForeignKey(x => x.SellerAccountId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<NotificationOutbox>(e =>
        {
            e.Property(x => x.EventKey).HasMaxLength(100);
            e.HasIndex(x => x.EventKey).IsUnique().HasFilter("[EventKey] IS NOT NULL");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
