using G4.Contracts.Checkout;
using G4.Infrastructure.Promotions;
using Microsoft.AspNetCore.Mvc;
namespace G4.Api.Controllers;
[ApiController]
[Route("api/{role}/promotions")]
public sealed class PromotionsController(ApplicationDbContext db, IHostEnvironment environment) : ApiControllerBase(environment)
{
    private bool Allowed(string role) => IsAvailable && (role == "seller" || role == "admin") && HasRole(role);
    [HttpGet("page")]
    public async Task<IActionResult> Page(string role, int page = 1, int pageSize = 10, string filter = "all", string? search = null, string type = "all", int? sellerId = null, string fundingSource = "all", CancellationToken ct = default)
    { if (!Allowed(role)) return StatusCode(403); return Ok(await new PromotionManagementService(db).ListAsync(CurrentUserId, role == "admin", page, pageSize, filter, search, type, sellerId, fundingSource, ct)); }
    [HttpGet("options")]
    public async Task<IActionResult> Options(string role, CancellationToken ct) { if (!Allowed(role)) return StatusCode(403); return Ok(await new PromotionManagementService(db).OptionsAsync(CurrentUserId, role == "admin", ct)); }
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(string role, int id, CancellationToken ct) { if (!Allowed(role)) return StatusCode(403); return Ok(await new PromotionManagementService(db).ReadAsync(CurrentUserId, role == "admin", id, ct)); }
    [HttpGet("{id:int}/audits")]
    public async Task<IActionResult> Audits(string role, int id, CancellationToken ct) { if (!Allowed(role)) return StatusCode(403); return Ok(await new PromotionManagementService(db).AuditsAsync(CurrentUserId, role == "admin", id, ct)); }
    [HttpPost]
    public async Task<IActionResult> Create(string role, PromotionInput input, CancellationToken ct) { if (!Allowed(role)) return StatusCode(403); return Ok(await new PromotionManagementService(db).SaveAsync(CurrentUserId, role == "admin", input, null, ct)); }
    [HttpPost("{id:int}/edit")]
    public async Task<IActionResult> Edit(string role, int id, PromotionInput input, CancellationToken ct) { if (!Allowed(role)) return StatusCode(403); return Ok(await new PromotionManagementService(db).SaveAsync(CurrentUserId, role == "admin", input, id, ct)); }
    [HttpPost("{id:int}/state")]
    public async Task<IActionResult> State(string role, int id, PromotionStateInput input, CancellationToken ct) { if (!Allowed(role)) return StatusCode(403); return Ok(await new PromotionManagementService(db).SetStateAsync(CurrentUserId, role == "admin", id, input, ct)); }
}
