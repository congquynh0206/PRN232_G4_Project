using G4.Contracts.Checkout;
using G4.Infrastructure.Finance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Controllers;
[ApiController]
[Route("api")]
public sealed class SellerLevelsController(ApplicationDbContext db, ISellerFinanceService finance,
    ISellerFeedbackService feedback, IConfiguration configuration, IHostEnvironment environment) : ApiControllerBase(environment)
{
    [HttpPost("orders/{id:int}/seller-feedback")]
    public async Task<IActionResult> Feedback(int id, SellerFeedbackInput input, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer")) return StatusCode(403);
        return Ok(await feedback.SubmitAsync(id, CurrentUserId, input, ct));
    }

    [HttpGet("seller/level")]
    public async Task<IActionResult> Level(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        return Ok(await finance.GetLevelDetailsAsync(CurrentUserId, ct));
    }

    [HttpGet("admin/seller-levels")]
    public async Task<IActionResult> Sellers(int page = 1, int pageSize = 10, int? sellerId = null, int? level = null, CancellationToken ct = default)
    {
        if (!IsAvailable || !HasRole("admin")) return StatusCode(403);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var query = db.SellerAccounts.AsNoTracking().Where(x => (!sellerId.HasValue || x.SellerId == sellerId)
            && (!level.HasValue || x.Level == level));
        var totalCount = await query.CountAsync(ct); page = Math.Clamp(page, 1, Math.Max(1, (totalCount + pageSize - 1) / pageSize));
        var items = await query.OrderBy(x => x.SellerId).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.SellerId, x.Level, x.Status, x.NegativeBalance, x.MonthlySalesLimit, x.HoldDays,
                x.LastLevelEvaluatedAt, sellerName = db.Users.Where(u => u.Id == x.SellerId).Select(u => u.Username).FirstOrDefault() }).ToListAsync(ct);
        return Ok(new { page, pageSize, totalCount, items, policy = SellerLevelRules.Policy(configuration) });
    }

    [HttpGet("admin/seller-levels/{sellerId:int}")]
    public async Task<IActionResult> Seller(int sellerId, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("admin")) return StatusCode(403);
        if (!await db.Users.AnyAsync(x => x.Id == sellerId && x.Role == "seller", ct)) return NotFound();
        return Ok(await finance.GetLevelDetailsAsync(sellerId, ct));
    }

    [HttpPost("dev/seller-levels/simulate")]
    public IActionResult Simulate(SellerLevelSimulation input)
    {
        if (!IsAvailable || !(HasRole("seller") || HasRole("admin"))) return StatusCode(403);
        var m = input.Metrics;
        if (m == null || m.CompletedOrders < 0 || m.PaidOrders < 0 || m.FeedbackCount < 0 || m.DeliveryCount < 0 || m.ActivityDays < 0
            || m.NegativeBalance < 0 || new decimal?[] { m.PositiveFeedbackRate, m.OnTimeDeliveryRate, m.CancellationRate, m.ReturnRate, m.DisputeRate }.Any(x => x < 0 || x > 100))
            return BadRequest(new { error = "Số liệu mô phỏng không hợp lệ." });
        return Ok(SellerLevelRules.Decide(input.CurrentLevel, m, SellerLevelRules.Policy(configuration)));
    }
}
