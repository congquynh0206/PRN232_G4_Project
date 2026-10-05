using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace G4.Infrastructure.Promotions;
public static class PromotionLifecycle
{
    public static async Task LockReservationsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction != null)
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='commerce:promotion-reservations', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @r<0 THROW 51000,'Khuyến mãi đang được xử lý. Hãy thử lại.',1;", ct);
    }
    public static async Task<IDbContextTransaction?> LockOrderAsync(ApplicationDbContext db, int orderId, CancellationToken ct)
    {
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction != null) return null;
        // Acquire the reservation gate before order locks and database reads so checkout,
        // cancellation, expiry and payment follow the same lock order.
        // ReadCommitted avoids unrelated payment keys sharing Serializable index-gap locks.
        // Coupon reservation itself remains Serializable in CreateOrderAsync.
        var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await LockReservationsAsync(db, ct);
            var resource = $"commerce:order:{orderId}";
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @r<0 THROW 51000,'Order is busy. Retry.',1;", ct);
            return tx;
        }
        catch { await tx.DisposeAsync(); throw; }
    }
    public static async Task ConsumeAsync(ApplicationDbContext db, int orderId, CancellationToken ct)
    {
        var usages = await db.PromotionUsages.Where(x => x.OrderId == orderId && x.State == "Reserved").ToListAsync(ct);
        foreach (var usage in usages) { usage.State = "Consumed"; usage.ConsumedAt = DateTime.UtcNow; }
    }
    public static async Task ReleaseAsync(ApplicationDbContext db, int orderId, CancellationToken ct)
    {
        var usages = await db.PromotionUsages.Where(x => x.OrderId == orderId && x.State == "Reserved").ToListAsync(ct);
        foreach (var usage in usages) { usage.State = "Released"; usage.ReleasedAt = DateTime.UtcNow; }
    }
}
