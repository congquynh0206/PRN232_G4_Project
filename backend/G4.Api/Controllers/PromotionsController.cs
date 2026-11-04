using G4.Api.Security;
using G4.Contracts.Checkout;
using G4.Infrastructure.Promotions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Controllers;

[ApiController]
[Route("api/{role}/promotions")]
public sealed class PromotionsController(ApplicationDbContext db, IHostEnvironment environment)
    : ApiControllerBase(environment)
{
    private bool Allowed(string role) =>
        IsAvailable && (role == "seller" || role == "admin") && HasRole(role);

    [HttpGet("offers")]
    public async Task<IActionResult> Offers(
        string role,
        string type = "all",
        int? sellerId = null,
        int page = 1,
        CancellationToken ct = default
    )
    {
        if (role != "buyer" || !IsAvailable || !HasRole(role))
            return StatusCode(403);
        if (page < 1 || page > int.MaxValue / 20)
            return BadRequest();

        const int size = 20;
        var now = DateTime.UtcNow;
        var query = db.Promotions.AsNoTracking().Where(p =>
            !p.IsPaused && !p.AdminPaused && p.StartAt <= now && p.EndAt > now
            && (p.Type != "Coupon" || p.IsPublicCoupon)
        );
        if (type == "Discount")
            query = query.Where(p => p.Type == "Sale" || p.Type == "Volume" || p.Type == "Order");
        else if (type != "all")
            query = query.Where(p => p.Type == type);
        if (sellerId.HasValue)
            query = query.Where(p => p.SellerId == sellerId || p.SellerId == null);
        else
            query = query.Where(p =>
                (p.MaxUsage == null || db.PromotionUsages.Count(u =>
                    u.PromotionId == p.Id && u.State != "Released") < p.MaxUsage)
                && (p.Budget == null || db.PromotionUsages.Where(u =>
                    u.PromotionId == p.Id && u.State != "Released").Sum(u => u.Amount) < p.Budget)
            );

        var offers = await query.OrderBy(p => p.EndAt).ThenBy(p => p.Id)
            .Skip((page - 1) * size).Take(size + 1)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Type,
                p.Code,
                p.SellerId,
                SellerName = db.Users.Where(u => u.Id == p.SellerId)
                    .Select(u => u.Stores.OrderBy(s => s.Id)
                        .Select(s => s.StoreName).FirstOrDefault() ?? u.Username)
                    .FirstOrDefault(),
                p.EndAt,
                p.Value,
                p.IsPercent,
                p.FreeShipping,
                p.Cap,
                p.MinSubtotal,
                p.MinQuantity,
                p.MaxUsagePerBuyer,
                Tiers = p.Tiers.OrderBy(t => t.MinQuantity)
                    .Select(t => new { t.MinQuantity, t.Percent }).ToList(),
                ProductCount = db.Products.Count(x =>
                    (!p.SellerId.HasValue || x.SellerId == p.SellerId)
                    && (!p.Targets.Any() || p.Targets.Any(t =>
                        t.ProductId == x.Id || (t.CategoryId != null && t.CategoryId == x.CategoryId)
                    ))
                ),
                Products = db.Products.Where(x =>
                    (!p.SellerId.HasValue || x.SellerId == p.SellerId)
                    && (!p.Targets.Any() || p.Targets.Any(t =>
                        t.ProductId == x.Id || (t.CategoryId != null && t.CategoryId == x.CategoryId)
                    ))
                ).OrderBy(x => x.Title).ThenBy(x => x.Id).Take(12)
                    .Select(x => new { x.Id, x.Title, x.Price }).ToList()
            }).ToListAsync(ct);

        return Ok(new { Items = offers.Take(size), HasMore = offers.Count > size });
    }

    [HttpGet("page")]
    public async Task<IActionResult> Page(
        string role,
        int page = 1,
        int pageSize = 10,
        string filter = "all",
        string? search = null,
        string type = "all",
        int? sellerId = null,
        string fundingSource = "all",
        CancellationToken ct = default
    )
    {
        if (!Allowed(role))
            return StatusCode(403);
        return Ok(
            await new PromotionManagementService(db).ListAsync(
                CurrentUserId,
                role == "admin",
                page,
                pageSize,
                filter,
                search,
                type,
                sellerId,
                fundingSource,
                ct
            )
        );
    }

    [HttpGet("options")]
    public async Task<IActionResult> Options(string role, CancellationToken ct)
    {
        if (!Allowed(role))
            return StatusCode(403);
        return Ok(
            await new PromotionManagementService(db).OptionsAsync(
                CurrentUserId,
                role == "admin",
                ct
            )
        );
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(string role, int id, CancellationToken ct)
    {
        if (!Allowed(role))
            return StatusCode(403);
        return Ok(
            await new PromotionManagementService(db).ReadAsync(
                CurrentUserId,
                role == "admin",
                id,
                ct
            )
        );
    }

    [HttpGet("{id:int}/audits")]
    public async Task<IActionResult> Audits(string role, int id, CancellationToken ct)
    {
        if (!Allowed(role))
            return StatusCode(403);
        return Ok(
            await new PromotionManagementService(db).AuditsAsync(
                CurrentUserId,
                role == "admin",
                id,
                ct
            )
        );
    }

    [HttpPost]
    [RequirePassword("seller,admin", "Tạo khuyến mãi")]
    public async Task<IActionResult> Create(string role, PromotionInput input, CancellationToken ct)
    {
        if (!Allowed(role))
            return StatusCode(403);
        return Ok(
            await new PromotionManagementService(db).SaveAsync(
                CurrentUserId,
                role == "admin",
                input,
                null,
                ct
            )
        );
    }

    [HttpPost("{id:int}/edit")]
    [RequirePassword("seller,admin", "Thay đổi khuyến mãi")]
    public async Task<IActionResult> Edit(
        string role,
        int id,
        PromotionInput input,
        CancellationToken ct
    )
    {
        if (!Allowed(role))
            return StatusCode(403);
        return Ok(
            await new PromotionManagementService(db).SaveAsync(
                CurrentUserId,
                role == "admin",
                input,
                id,
                ct
            )
        );
    }

    [HttpPost("{id:int}/state")]
    [RequirePassword("seller,admin", "Bật hoặc tắt khuyến mãi")]
    public async Task<IActionResult> State(
        string role,
        int id,
        PromotionStateInput input,
        CancellationToken ct
    )
    {
        if (!Allowed(role))
            return StatusCode(403);
        return Ok(
            await new PromotionManagementService(db).SetStateAsync(
                CurrentUserId,
                role == "admin",
                id,
                input,
                ct
            )
        );
    }

    [HttpPost("{id:int}/suspension")]
    [RequirePassword("admin", "Đình chỉ hoặc gỡ đình chỉ khuyến mãi")]
    public async Task<IActionResult> Suspension(
        string role,
        int id,
        PromotionSuspensionInput input,
        CancellationToken ct
    )
    {
        if (role != "admin" || !Allowed(role))
            return StatusCode(403);
        return Ok(
            await new PromotionManagementService(db).SetSuspensionAsync(
                CurrentUserId,
                id,
                input,
                ct
            )
        );
    }
}
