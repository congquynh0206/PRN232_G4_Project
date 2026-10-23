using G4.Contracts.Checkout;
using G4.Infrastructure.Finance;
using Microsoft.AspNetCore.Mvc;

namespace G4.Api.Controllers;

[ApiController]
[Route("api/admin/platform-fees")]
public sealed class PlatformFeesController(
    ApplicationDbContext db,
    TimeProvider clock,
    IHostEnvironment environment
) : ApiControllerBase(environment)
{
    [HttpGet]
    public async Task<IActionResult> Read(
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default
    )
    {
        if (!IsAvailable || !HasRole("admin"))
            return StatusCode(403);
        return Ok(
            await new PlatformFeeService(db, clock).ReadAsync(CurrentUserId, page, pageSize, ct)
        );
    }

    [HttpPost]
    public async Task<IActionResult> Change(PlatformFeeChangeRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("admin"))
            return StatusCode(403);
        return Ok(
            new
            {
                id = await new PlatformFeeService(db, clock).ChangeAsync(
                    CurrentUserId,
                    request,
                    ct
                ),
            }
        );
    }
}
