using G4.Contracts.Checkout;
using G4.Infrastructure.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace G4.Api.Controllers;

[ApiController]
[Route("api/integration-logs")]
public sealed class IntegrationLogsController(ApplicationDbContext db,IHostEnvironment environment):ApiControllerBase(environment)
{
    [HttpGet]
    public async Task<IActionResult> Logs([FromQuery] IntegrationLogQueryOptions query,CancellationToken ct)
    {
        if(!IsAvailable)return NotFound();
        var role=HasRole("admin") ? "admin" : HasRole("seller") ? "seller" : "buyer";
        return Ok(await new DiagnosticsQueryService(db).LogsAsync(CurrentUserId,role,query,ct));
    }
}
