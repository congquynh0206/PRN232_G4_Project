using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Data;
namespace G4.Infrastructure.Promotions;

public sealed class PromotionManagementService(ApplicationDbContext db)
{
    public async Task<PromotionView> SaveAsync(int actor, bool admin, PromotionInput input, int? id = null, CancellationToken ct = default)
    {
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        await PromotionLifecycle.LockReservationsAsync(db, ct);
        if (id.HasValue && db.Database.IsRelational()) await db.Promotions.FromSqlInterpolated($"SELECT * FROM Promotion WITH (UPDLOCK,HOLDLOCK) WHERE Id={id.Value}").AsNoTracking().ToListAsync(ct);
        var p = id.HasValue ? await db.Promotions.Include(x => x.Targets).Include(x => x.Tiers).SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        if (id.HasValue && p == null) throw new KeyNotFoundException("Không tìm thấy khuyến mãi.");
        if (p != null) { EnsureOwner(p, actor, admin); if (p.Version != input.Version) throw new InvalidOperationException("Khuyến mãi đã thay đổi. Hãy tải lại."); if (p.Type != input.Type) throw new ArgumentException("Không thể đổi loại khuyến mãi."); }
        if (!new[] { "Sale", "Volume", "Order", "Coupon", "Shipping" }.Contains(input.Type) || admin && input.Type != "Coupon") throw new ArgumentException("Loại khuyến mãi không hợp lệ.");
        var code = input.Type == "Coupon" ? NormalizeCode(input.Code) : null;
        if (p != null && p.Code != code) throw new ArgumentException("Không thể đổi mã coupon.");
        if (code != null && await db.Promotions.AnyAsync(x => x.Code == code && x.Id != (id ?? 0), ct)) throw new InvalidOperationException("Mã coupon đã tồn tại.");
        Validate(input); var owner = admin ? input.SellerId : actor;
        if (p != null && p.SellerId != owner) throw new ArgumentException("Không thể đổi shop của khuyến mãi.");
        if (owner.HasValue && !await db.Users.AnyAsync(x => x.Id == owner && x.Role == "seller", ct)) throw new ArgumentException("Shop không hợp lệ.");
        var products = input.ProductIds.Distinct().ToArray(); var categories = input.CategoryIds.Distinct().ToArray();
        if (products.Length > 0 && categories.Length > 0) throw new ArgumentException("Chọn sản phẩm hoặc danh mục.");
        if (await db.Products.CountAsync(x => products.Contains(x.Id) && (!owner.HasValue || x.SellerId == owner), ct) != products.Length) throw new UnauthorizedAccessException("Có sản phẩm không thuộc shop được chọn.");
        if (await db.Categories.CountAsync(x => categories.Contains(x.Id) && (!owner.HasValue || db.Products.Any(p => p.CategoryId == x.Id && p.SellerId == owner)), ct) != categories.Length) throw new ArgumentException("Danh mục không hợp lệ hoặc không có sản phẩm thuộc shop.");
        if (p != null)
        {
            if (p.AdminPaused && !input.IsPaused) throw new UnauthorizedAccessException("Khuyến mãi đang bị admin tạm dừng.");
            var usage = await db.PromotionUsages.Where(x => x.PromotionId == p.Id && x.State != "Released").ToListAsync(ct);
            if (input.MaxUsage.HasValue && input.MaxUsage < usage.Count || input.Budget.HasValue && input.Budget < usage.Sum(x => x.Amount) || input.MaxUsagePerBuyer.HasValue && usage.GroupBy(x => x.BuyerId).Any(g => g.Count() > input.MaxUsagePerBuyer)) throw new ArgumentException("Giới hạn mới thấp hơn lượt dùng hoặc ngân sách đang được giữ.");
            db.PromotionTargets.RemoveRange(p.Targets); db.PromotionTiers.RemoveRange(p.Tiers); p.Targets = []; p.Tiers = []; p.Version++;
        }
        else { p = new Promotion { SellerId = owner, FundingSource = admin ? "Platform" : "Seller", CreatedById = actor, CreatedAt = DateTime.UtcNow }; db.Promotions.Add(p); }
        p.Name = input.Name.Trim(); p.Type = input.Type; p.Code = code; p.Value = input.Type == "Volume" || input.FreeShipping ? 0 : input.Value;
        p.IsPercent = input.IsPercent; p.FreeShipping = input.Type == "Shipping" && input.FreeShipping; p.Cap = input.Cap; p.IsPaused = input.IsPaused;
        p.MinSubtotal = input.MinSubtotal; p.MinQuantity = Math.Max(1, input.MinQuantity); p.StartAt = AsUtc(input.StartAt); p.EndAt = AsUtc(input.EndAt);
        p.MaxUsage = input.MaxUsage; p.MaxUsagePerBuyer = input.MaxUsagePerBuyer; p.Budget = input.Budget; p.UpdatedAt = DateTime.UtcNow;
        p.Targets.AddRange(products.Select(x => new PromotionTarget { ProductId = x })); p.Targets.AddRange(categories.Select(x => new PromotionTarget { CategoryId = x }));
        if (input.Type == "Volume") p.Tiers.AddRange(input.Tiers.Select(x => new PromotionTier { MinQuantity = x.MinQuantity, Percent = x.Percent }));
        await db.SaveChangesAsync(ct);
        db.PromotionAudits.Add(new PromotionAudit { PromotionId = p.Id, ActorId = actor, Action = id.HasValue ? "Edit" : "Create", ChangesJson = JsonSerializer.Serialize(input), CreatedAt = DateTime.UtcNow }); await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return await ViewAsync(p, ct);
    }
    public async Task<PromotionView> SetStateAsync(int actor, bool admin, int id, PromotionStateInput input, CancellationToken ct = default)
    {
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        await PromotionLifecycle.LockReservationsAsync(db, ct);
        if (db.Database.IsRelational()) await db.Promotions.FromSqlInterpolated($"SELECT * FROM Promotion WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").AsNoTracking().ToListAsync(ct);
        var p = await db.Promotions.Include(x => x.Targets).Include(x => x.Tiers).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy khuyến mãi.");
        if (!admin) EnsureOwner(p, actor, false);
        if (p.Version != input.Version) throw new InvalidOperationException("Khuyến mãi đã thay đổi. Hãy tải lại.");
        if (!admin && p.AdminPaused && !input.Paused) throw new UnauthorizedAccessException("Khuyến mãi bị admin tạm dừng. Liên hệ admin để mở lại.");
        if (admin && p.FundingSource == "Seller" && input.Paused && string.IsNullOrWhiteSpace(input.Reason)) throw new ArgumentException("Nhập lý do tạm dừng.");
        if (input.Reason?.Length > 1000) throw new ArgumentException("Lý do tối đa 1000 ký tự.");
        p.IsPaused = input.Paused; if (admin) p.AdminPaused = input.Paused; p.PauseReason = input.Paused ? input.Reason?.Trim() : null; p.Version++; p.UpdatedAt = DateTime.UtcNow;
        db.PromotionAudits.Add(new PromotionAudit { PromotionId = id, ActorId = actor, Action = input.Paused ? "Pause" : "Resume", Reason = input.Reason, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(ct); if (tx != null) await tx.CommitAsync(ct); return await ViewAsync(p, ct);
    }
    public async Task<PromotionView> ReadAsync(int actor, bool admin, int id, CancellationToken ct = default)
    { var p = await db.Promotions.AsNoTracking().Include(x => x.Targets).Include(x => x.Tiers).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy khuyến mãi."); if (!admin) EnsureOwner(p, actor, false); return await ViewAsync(p, ct); }
    public async Task<object> AuditsAsync(int actor, bool admin, int id, CancellationToken ct = default)
    {
        await ReadAsync(actor, admin, id, ct);
        return await db.PromotionAudits.AsNoTracking().Where(x => x.PromotionId == id).OrderByDescending(x => x.Id)
            .Join(db.Users, x => x.ActorId, u => u.Id, (x, u) => new { x.Id, x.ActorId, actorName = u.Username, x.Action, x.Reason, x.CreatedAt, x.ChangesJson }).ToListAsync(ct);
    }
    public async Task<PromotionPage> ListAsync(int actor, bool admin, int page = 1, int pageSize = 10, string filter = "all", string? search = null, string type = "all", int? sellerId = null, string fundingSource = "all", CancellationToken ct = default)
    {
        var now = DateTime.UtcNow; var scope = db.Promotions.AsNoTracking().Where(x => admin || x.SellerId == actor && x.FundingSource == "Seller");
        if (admin && sellerId.HasValue) scope = scope.Where(x => x.SellerId == sellerId); if (fundingSource != "all") scope = scope.Where(x => x.FundingSource == fundingSource);
        var counts = new PromotionCounts(await scope.CountAsync(ct), await scope.CountAsync(x => !x.IsPaused && x.StartAt > now, ct), await scope.CountAsync(x => !x.IsPaused && x.StartAt <= now && x.EndAt > now, ct), await scope.CountAsync(x => x.IsPaused, ct), await scope.CountAsync(x => !x.IsPaused && x.EndAt <= now, ct));
        scope = filter switch { "scheduled" => scope.Where(x => !x.IsPaused && x.StartAt > now), "active" => scope.Where(x => !x.IsPaused && x.StartAt <= now && x.EndAt > now), "paused" => scope.Where(x => x.IsPaused), "ended" => scope.Where(x => !x.IsPaused && x.EndAt <= now), _ => scope };
        if (!string.IsNullOrWhiteSpace(search)) { var s = search.Trim(); scope = scope.Where(x => x.Name.Contains(s) || x.Code != null && x.Code.Contains(s)); }
        if (type != "all") scope = scope.Where(x => x.Type == type);
        pageSize = Math.Clamp(pageSize, 1, 50); var total = await scope.CountAsync(ct); page = Math.Clamp(page, 1, Math.Max(1, (total + pageSize - 1) / pageSize));
        var rows = await scope.Include(x => x.Targets).Include(x => x.Tiers).OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var views = new List<PromotionView>(); foreach (var p in rows) views.Add(await ViewAsync(p, ct)); return new(page, pageSize, total, counts, views);
    }
    public async Task<object> OptionsAsync(int actor, bool admin, CancellationToken ct = default) => new
    {
        products = await db.Products.Where(x => admin || x.SellerId == actor).Select(x => new { x.Id, x.Title, x.CategoryId, x.SellerId }).ToListAsync(ct),
        categories = await db.Categories.Where(x => admin || db.Products.Any(p => p.SellerId == actor && p.CategoryId == x.Id)).Select(x => new { x.Id, name = x.Name }).ToListAsync(ct),
        sellers = await db.Users.Where(x => x.Role == "seller" && (admin || x.Id == actor)).Select(x => new { x.Id, name = x.Username }).ToListAsync(ct)
    };
    private async Task<PromotionView> ViewAsync(Promotion p, CancellationToken ct)
    {
        var usage = await db.PromotionUsages.AsNoTracking().Where(x => x.PromotionId == p.Id && x.State != "Released").ToListAsync(ct); var now = DateTime.UtcNow;
        return new()
        {
            Id = p.Id,
            Name = p.Name,
            Type = p.Type,
            Code = p.Code,
            FundingSource = p.FundingSource,
            SellerId = p.SellerId,
            Value = p.Value,
            IsPercent = p.IsPercent,
            FreeShipping = p.FreeShipping,
            Cap = p.Cap,
            MinSubtotal = p.MinSubtotal,
            MinQuantity = p.MinQuantity,
            StartAt = p.StartAt,
            EndAt = p.EndAt,
            Version = p.Version,
            IsPaused = p.IsPaused,
            AdminPaused = p.AdminPaused,
            PauseReason = p.PauseReason,
            MaxUsage = p.MaxUsage,
            MaxUsagePerBuyer = p.MaxUsagePerBuyer,
            Budget = p.Budget,
            ProductIds = p.Targets.Where(x => x.ProductId.HasValue).Select(x => x.ProductId!.Value).ToArray(),
            CategoryIds = p.Targets.Where(x => x.CategoryId.HasValue).Select(x => x.CategoryId!.Value).ToArray(),
            Tiers = p.Tiers.OrderBy(x => x.MinQuantity).Select(x => new PromotionTierInput(x.MinQuantity, x.Percent)).ToArray(),
            Status = p.IsPaused ? "Paused" : p.StartAt > now ? "Scheduled" : p.EndAt <= now ? "Ended" : "Active",
            UsedCount = usage.Count(x => x.State == "Consumed"),
            ReservedCount = usage.Count(x => x.State == "Reserved"),
            Spent = usage.Where(x => x.State == "Consumed").Sum(x => x.Amount),
            ReservedAmount = usage.Where(x => x.State == "Reserved").Sum(x => x.Amount)
        };
    }
    private static void EnsureOwner(Promotion p, int actor, bool admin) { if (admin ? p.FundingSource != "Platform" : p.SellerId != actor || p.FundingSource != "Seller") throw new UnauthorizedAccessException("Bạn không có quyền sửa khuyến mãi này."); }
    internal static string NormalizeCode(string? code) { var value = (code ?? "").Trim().ToUpperInvariant(); if (!Regex.IsMatch(value, "^[A-Z0-9_-]{3,50}$")) throw new ArgumentException("Mã coupon gồm 3–50 chữ cái, chữ số, dấu _ hoặc -."); return value; }
    private static DateTime AsUtc(DateTime d) => d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : DateTime.SpecifyKind(d, DateTimeKind.Utc);
    private static void Validate(PromotionInput x)
    {
        if (x.ProductIds == null || x.CategoryIds == null || x.Tiers == null) throw new ArgumentException("Phạm vi khuyến mãi không hợp lệ.");
        if (new[] { x.Value, x.MinSubtotal, x.Cap ?? 0, x.Budget ?? 0 }.Any(v => v > 999999999999m || decimal.Round(v, 2) != v)) throw new ArgumentException("Giá trị có tối đa 2 chữ số thập phân và không vượt 999999999999.");
        if (x.MaxUsage.HasValue && x.MaxUsagePerBuyer > x.MaxUsage) throw new ArgumentException("Giới hạn mỗi buyer không được vượt tổng lượt.");
        if (string.IsNullOrWhiteSpace(x.Name) || x.Name.Trim().Length > 150) throw new ArgumentException("Tên khuyến mãi dài 1–150 ký tự.");
        if (x.StartAt == default || x.EndAt <= x.StartAt) throw new ArgumentException("Thời gian kết thúc phải sau thời gian bắt đầu.");
        if (x.MinSubtotal < 0 || x.MinQuantity < 1 || x.Cap <= 0 || x.Budget <= 0 || x.MaxUsage <= 0 || x.MaxUsagePerBuyer <= 0) throw new ArgumentException("Điều kiện và giới hạn không hợp lệ.");
        if (x.FreeShipping && x.Type != "Shipping") throw new ArgumentException("Miễn phí vận chuyển chỉ áp dụng cho loại Shipping.");
        if (x.Type == "Volume")
        { if (x.Tiers.Length == 0 || x.Tiers.Any(t => t.MinQuantity < 1 || t.Percent <= 0 || t.Percent > 100) || x.Tiers.Select(t => t.MinQuantity).Distinct().Count() != x.Tiers.Length) throw new ArgumentException("Các mức số lượng và phần trăm không hợp lệ."); var tiers = x.Tiers.OrderBy(t => t.MinQuantity).ToArray(); if (tiers.Zip(tiers.Skip(1), (a, b) => b.Percent < a.Percent).Any(v => v)) throw new ArgumentException("Mức giảm phải tăng theo số lượng."); }
        else if (!x.FreeShipping && (x.Value <= 0 || x.IsPercent && x.Value > 100)) throw new ArgumentException("Giá trị giảm phải dương; phần trăm tối đa 100.");
    }
}
