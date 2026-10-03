using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<DisputeEntry> DisputeEntries => Set<DisputeEntry>();

    private void GuardDisputeHistory()
    {
        if (ChangeTracker.Entries<DisputeEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Bằng chứng và lịch sử không thể sửa hoặc xóa sau khi gửi.");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardDisputeHistory();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardDisputeHistory();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
