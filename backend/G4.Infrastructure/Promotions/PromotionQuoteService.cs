using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace G4.Infrastructure.Promotions;
internal sealed class PromotionQuoteService(ApplicationDbContext db)
{
    public async Task<PriceQuote> CalculateAsync(int buyer, int seller, IReadOnlyList<PromotionPriceLine> lines, decimal shipping, int addressId, string addresses, string? coupon, bool locked, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (locked && db.Database.IsRelational())
        {
            var ids = await db.Promotions.AsNoTracking().Where(x => x.SellerId == seller || x.FundingSource == "Platform" && x.SellerId == null).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
            foreach (var id in ids) await db.Promotions.FromSqlInterpolated($"SELECT * FROM Promotion WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").AsNoTracking().ToListAsync(ct);
        }
        var programs = await db.Promotions.AsNoTracking().Include(x => x.Targets).Include(x => x.Tiers)
            .Where(x => !x.IsPaused && x.StartAt <= now && x.EndAt > now && (x.SellerId == seller || x.FundingSource == "Platform" && x.SellerId == null)).ToListAsync(ct);
        var normalized = string.IsNullOrWhiteSpace(coupon) ? null : PromotionManagementService.NormalizeCode(coupon);
        var ids2 = programs.Select(x => x.Id).ToArray(); var uses = await db.PromotionUsages.AsNoTracking().Where(x => ids2.Contains(x.PromotionId) && x.State != "Released").ToListAsync(ct);
        var eligible = programs.Where(p => (!p.MaxUsage.HasValue || uses.Count(u => u.PromotionId == p.Id) < p.MaxUsage) && (!p.MaxUsagePerBuyer.HasValue || uses.Count(u => u.PromotionId == p.Id && u.BuyerId == buyer) < p.MaxUsagePerBuyer) && (!p.Budget.HasValue || uses.Where(u => u.PromotionId == p.Id).Sum(u => u.Amount) < p.Budget)).ToList();
        if (normalized != null)
        {
            var code = await db.Promotions.AsNoTracking().SingleOrDefaultAsync(x => x.Code == normalized, ct) ?? throw new ArgumentException("Mã khuyến mãi không tồn tại.");
            if (code.SellerId.HasValue && code.SellerId != seller) throw new ArgumentException("Mã khuyến mãi không áp dụng cho shop này.");
            if (code.IsPaused) throw new ArgumentException("Mã khuyến mãi đang tạm dừng.");
            if (code.StartAt > now) throw new ArgumentException("Mã khuyến mãi chưa đến thời gian sử dụng.");
            if (code.EndAt <= now) throw new ArgumentException("Mã khuyến mãi đã hết hạn.");
            if (code.MaxUsage.HasValue && uses.Count(u => u.PromotionId == code.Id) >= code.MaxUsage) throw new ArgumentException("Mã khuyến mãi đã hết tổng lượt sử dụng.");
            if (code.MaxUsagePerBuyer.HasValue && uses.Count(u => u.PromotionId == code.Id && u.BuyerId == buyer) >= code.MaxUsagePerBuyer) throw new ArgumentException("Bạn đã dùng hết lượt của mã khuyến mãi này.");
            if (code.Budget.HasValue && uses.Where(u => u.PromotionId == code.Id).Sum(u => u.Amount) >= code.Budget) throw new ArgumentException("Mã khuyến mãi đã hết ngân sách.");
        }
        PromotionPriceResult result;
        while (true)
        {
            result = PromotionPricing.Calculate(lines, eligible.Select(Rule).ToArray(), shipping, normalized);
            var over = result.Promotions.Where(a => { var p = eligible.Single(x => x.Id == a.Id); return p.Budget.HasValue && a.Amount + uses.Where(u => u.PromotionId == p.Id).Sum(u => u.Amount) > p.Budget; }).ToArray();
            if (over.Length == 0) break;
            if (over.Any(x => x.Type == "Coupon")) throw new ArgumentException("Coupon không còn đủ ngân sách cho đơn này.");
            var remove = over.Select(x => x.Id).ToArray(); eligible.RemoveAll(x => remove.Contains(x.Id));
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { buyer, seller, addressId, addresses, coupon = normalized, lines = lines.OrderBy(x => x.ProductId), shipping, result }))));
        return new(result.Subtotal, result.GoodsDiscount, result.Shipping, result.Total, result.Weight) { ShippingBase = result.ShippingBase, ShippingDiscount = result.ShippingDiscount, SellerDiscount = result.SellerGoodsDiscount, PlatformSubsidy = result.PlatformSubsidy, SellerGross = result.SellerGross, Lines = result.Lines, Promotions = result.Promotions, PricingFingerprint = fingerprint };
    }
    private static PromotionRule Rule(Promotion p) => new(p.Id, p.Name, p.Type, p.FundingSource, p.SellerId, p.Code, p.Value, p.IsPercent, p.FreeShipping, p.Cap, p.MinSubtotal, p.MinQuantity, p.Version,
        p.Targets.Where(x => x.ProductId.HasValue).Select(x => x.ProductId!.Value).ToArray(), p.Targets.Where(x => x.CategoryId.HasValue).Select(x => x.CategoryId!.Value).ToArray(), p.Tiers.Select(x => new PromotionTierRule(x.MinQuantity, x.Percent)).ToArray());
}
