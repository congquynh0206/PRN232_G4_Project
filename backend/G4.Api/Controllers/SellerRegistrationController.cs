using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class SellerRegistrationController(
    ISellerRegistrationService registration,
    ApplicationDbContext db,
    IHostEnvironment environment
) : ApiControllerBase(environment)
{
    [AllowAnonymous]
    [HttpPost("auth/register-seller")]
    public async Task<IActionResult> Register(SellerRegistrationInput input, CancellationToken ct)
    {
        if (!IsAvailable)
            return NotFound();
        return Ok(await registration.RegisterAsync(input, ct));
    }

    [AllowAnonymous]
    [HttpGet("auth/seller-registration-samples")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Samples(CancellationToken ct)
    {
        if (!IsAvailable)
            return NotFound();
        return Ok(await registration.SamplesAsync(ct));
    }

    [HttpGet("seller/identity")]
    public async Task<IActionResult> Identity(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        return Ok(await registration.ProfileAsync(CurrentUserId, ct));
    }

    [HttpPost("seller/identity")]
    public async Task<IActionResult> Identity(SellerIdentityChangeInput input, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        return Ok(await registration.ChangeIdentityAsync(CurrentUserId, input, ct));
    }

    [HttpGet("admin/seller-identities/{sellerId:int}")]
    public async Task<IActionResult> AdminIdentity(int sellerId, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("admin"))
            return StatusCode(403);
        return Ok(await registration.ProfileAsync(sellerId, ct));
    }

    [HttpGet("admin/seller-blacklist")]
    public async Task<IActionResult> Blacklist(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] int? sellerId = null,
        CancellationToken ct = default
    )
    {
        if (!IsAvailable || !HasRole("admin"))
            return StatusCode(403);
        var query = db.SellerBlacklist.AsNoTracking().AsQueryable();
        if (sellerId != null)
            query = query.Where(x => x.SourceSellerId == sellerId);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var totalCount = await query.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (totalCount + pageSize - 1) / pageSize));
        var items = await query
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.Kind,
                x.MaskedValue,
                x.SourceSellerId,
                x.WriteOffId,
                x.IsDemo,
                x.Reason,
                x.CreatedAt,
            })
            .ToListAsync(ct);
        return Ok(
            new
            {
                page,
                pageSize,
                totalCount,
                items,
            }
        );
    }
}
