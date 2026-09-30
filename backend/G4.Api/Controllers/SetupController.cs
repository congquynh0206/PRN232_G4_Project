

using Microsoft.AspNetCore.Mvc;

namespace G4.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class SetupController(ApplicationDbContext db, IHostEnvironment environment) : ApiControllerBase(environment)
{
    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap(CancellationToken ct)
    {
        if (!IsAvailable) return NotFound();
        await ApplicationDataSeeder.EnsureAsync(db, ct);
        return Ok(new { message = "Application data ready" });
    }
}
