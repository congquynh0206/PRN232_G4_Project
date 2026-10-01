
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace G4.Api.Controllers;

[ApiController]
[Route("api/carrier")]
[AllowAnonymous]
public sealed class CarrierSimulatorController(IConfiguration configuration, IHostEnvironment environment, CarrierAvailabilityState state) : ControllerBase
{
    public sealed record LabelRequest(int OrderId, string Direction, string Key);

    [HttpPost("labels")]
    public IActionResult CreateLabel(LabelRequest request)
    {
        if (!environment.IsDevelopment()) return NotFound();
        var expected = configuration["Carrier:Key"] ?? "local-carrier-key";
        if (!Request.Headers.TryGetValue("X-Carrier-Key", out var supplied) || supplied != expected)
            return Unauthorized();
        if (request.OrderId <= 0 || request.Direction is not ("Outbound" or "Return") ||
            request.Key != $"ship-{request.OrderId}-{request.Direction.ToLowerInvariant()}")
            return BadRequest();
        if (state.ShouldFail(request.Key)) return StatusCode(503, new { message = "Simulated carrier outage" });
        return Ok(new { trackingNumber = $"G4-{request.OrderId}-{(request.Direction == "Outbound" ? "O" : "R")}" });
    }
}
