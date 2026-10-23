using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Finance;

public sealed class PlatformFeeService(ApplicationDbContext db, TimeProvider? clock = null)
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    internal async Task<PlatformFeePolicy?> PolicyAtAsync(DateTime at, CancellationToken ct) =>
        await db
            .PlatformFeePolicies.AsNoTracking()
            .Where(x => x.EffectiveAt <= at)
            .OrderByDescending(x => x.EffectiveAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<PlatformFeeSettings> ReadAsync(
        int adminId,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default
    )
    {
        await RequireAdminAsync(adminId, ct);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var count = await db.PlatformFeePolicies.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (count + pageSize - 1) / pageSize));
        var current = await PolicyAtAsync(Now, ct) ?? DefaultPolicy();
        var latest = await db.PlatformFeePolicies.MaxAsync(x => (int?)x.Id, ct) ?? 0;
        var rows = await db
            .PlatformFeePolicies.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var ids = rows.Append(current)
            .Where(x => x.ChangedById != null)
            .Select(x => x.ChangedById!.Value)
            .Distinct()
            .ToArray();
        var names = await db
            .Users.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Username ?? "Admin", ct);
        PlatformFeePolicyView View(PlatformFeePolicy x) =>
            new(
                x.Id,
                x.FeePercent,
                x.FixedFee,
                x.PreviousFeePercent,
                x.PreviousFixedFee,
                x.EffectiveAt,
                x.CreatedAt,
                x.ChangedById,
                x.ChangedById is int id ? names.GetValueOrDefault(id, "Admin") : "Hệ thống",
                x.Reason
            );
        return new(View(current), latest, page, pageSize, count, rows.Select(View).ToArray());
    }

    public async Task<int> ChangeAsync(
        int adminId,
        PlatformFeeChangeRequest request,
        CancellationToken ct = default
    )
    {
        await RequireAdminAsync(adminId, ct);
        if (
            request.FeePercent is < 0 or > 100
            || request.FeePercent != decimal.Round(request.FeePercent, 2)
        )
            throw new ArgumentException(
                "Phần trăm phí phải từ 0 đến 100, tối đa 2 chữ số thập phân."
            );
        if (
            request.FixedFee is < 0 or > 10000
            || request.FixedFee != decimal.Round(request.FixedFee, 2)
        )
            throw new ArgumentException(
                "Phí cố định phải từ 0 đến 10.000 USD, tối đa 2 chữ số thập phân."
            );
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            throw new ArgumentException("Nhập lý do thay đổi, tối đa 1.000 ký tự.");
        if (string.IsNullOrWhiteSpace(request.RequestKey) || request.RequestKey.Length > 100)
            throw new ArgumentException("Khóa yêu cầu không hợp lệ.");
        await using var tx =
            db.Database.IsRelational() && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable,
                    ct
                )
                : null;
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource = N'commerce:platform-fee',
                    @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 10000;
                IF @result < 0 THROW 51000, 'Cannot lock platform fee configuration', 1;",
                ct
            );
        var existing = await db
            .PlatformFeePolicies.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RequestKey == request.RequestKey, ct);
        if (existing != null)
        {
            if (
                existing.ChangedById != adminId
                || existing.FeePercent != request.FeePercent
                || existing.FixedFee != request.FixedFee
                || existing.Reason != request.Reason.Trim()
                || request.EffectiveAt != null
                    && existing.EffectiveAt != request.EffectiveAt.Value.UtcDateTime
            )
                throw new InvalidOperationException("Khóa yêu cầu đã dùng cho thay đổi khác.");
            return existing.Id;
        }
        var latest = await db.PlatformFeePolicies.MaxAsync(x => (int?)x.Id, ct) ?? 0;
        if (latest != request.ExpectedLatestPolicyId)
            throw new InvalidOperationException(
                "Cấu hình đã thay đổi ở phiên khác. Tải lại trước khi lưu."
            );
        var now = Now;
        var effective = request.EffectiveAt?.UtcDateTime ?? now;
        if (effective < now)
            throw new ArgumentException("Thời điểm hiệu lực không được nằm trong quá khứ.");
        if (await db.PlatformFeePolicies.AnyAsync(x => x.EffectiveAt == effective, ct))
            throw new InvalidOperationException(
                "Đã có mức phí tại thời điểm này. Chọn thời điểm khác."
            );
        var previous = await PolicyAtAsync(effective, ct) ?? DefaultPolicy();
        var change = new PlatformFeePolicy
        {
            FeePercent = request.FeePercent,
            FixedFee = request.FixedFee,
            PreviousFeePercent = previous.FeePercent,
            PreviousFixedFee = previous.FixedFee,
            EffectiveAt = effective,
            CreatedAt = now,
            ChangedById = adminId,
            Reason = request.Reason.Trim(),
            RequestKey = request.RequestKey,
        };
        db.PlatformFeePolicies.Add(change);
        await db.SaveChangesAsync(ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return change.Id;
    }

    private async Task RequireAdminAsync(int id, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == id && x.Role == "admin", ct))
            throw new UnauthorizedAccessException("Chỉ admin được quản lý cấu hình phí.");
    }

    private static PlatformFeePolicy DefaultPolicy() => new() { FeePercent = 12m, FixedFee = .30m };
}
