using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G4.Api.Controllers;
[ApiController]
[Route("api/admin/seller-write-offs")]
public sealed class SellerWriteOffController(ApplicationDbContext db, IConfiguration config,
    IHostEnvironment environment) : ApiControllerBase(environment)
{
    [HttpGet]
    public async Task<IActionResult> Decisions([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery] int? sellerId = null, CancellationToken ct = default)
    {
        if (!IsAvailable || !HasRole("admin")) return StatusCode(403);
        var query = db.SellerDebtWriteOffs.AsNoTracking();
        if (sellerId != null) query = query.Where(x => db.SellerAccounts.Any(a => a.Id == x.SellerAccountId && a.SellerId == sellerId));
        pageSize = Math.Clamp(pageSize, 1, 50);
        var totalCount = await query.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (totalCount + pageSize - 1) / pageSize));
        var amounts = query.Select(x => new { Decision = x,
            Recovered = db.SellerWriteOffRecoveries.Where(r => r.WriteOffId == x.Id).Sum(r => r.Amount),
            Account = db.SellerAccounts.Single(a => a.Id == x.SellerAccountId) });
        var summary = new {
            originalAmount = await query.SumAsync(x => x.OriginalAmount, ct),
            recoveredAmount = await db.SellerWriteOffRecoveries.Where(r => query.Any(w => w.Id == r.WriteOffId)).SumAsync(r => r.Amount, ct)
        };
        var items = await amounts.OrderByDescending(x => x.Decision.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new SellerWriteOffView(x.Decision.Id, x.Account.SellerId, x.Decision.CycleNumber,
                x.Decision.OriginalAmount, x.Recovered, x.Decision.OriginalAmount - x.Recovered, x.Account.NegativeBalance,
                x.Decision.CreatedAt, x.Decision.DebtStartedAt, x.Decision.DebtDueAt, x.Decision.EligibleAt,
                x.Decision.LastRecoveryAt, x.Decision.RecoveryGraceSeconds, x.Decision.BlacklistedIdentities,
                x.Decision.Actor, x.Decision.Reason)).ToListAsync(ct);
        return Ok(new { page, pageSize, totalCount, items, summary, policy = new {
            enabled = config.GetValue("Finance:WriteOff:Enabled", true),
            afterSeconds = config.GetValue("Finance:WriteOff:AfterSeconds", 30 * 86400),
            inactivitySeconds = config.GetValue("Finance:WriteOff:InactivitySeconds", 7 * 86400)
        } });
    }
}
