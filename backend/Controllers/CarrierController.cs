using backend.Checkout;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/carrier")]
public sealed class CarrierController(IConfiguration configuration, IHostEnvironment environment, DemoCarrierState state) : ControllerBase
{
    public sealed record LabelRequest(int OrderId, string Direction, string Key);

    [HttpPost("labels")]
    public IActionResult CreateLabel(LabelRequest request)
    {
        if (!environment.IsDevelopment()) return NotFound();
        var expected = configuration["Carrier:Key"] ?? "demo-only-carrier-key";
        if (!Request.Headers.TryGetValue("X-Carrier-Key", out var supplied) || supplied != expected)
            return Unauthorized();
        if (request.OrderId <= 0 || request.Direction is not ("Outbound" or "Return") ||
            request.Key != $"ship-{request.OrderId}-{request.Direction.ToLowerInvariant()}")
            return BadRequest();
        if (state.ShouldFail(request.Key)) return StatusCode(503, new { message = "Simulated carrier outage" });
        return Ok(new { trackingNumber = $"G4-{request.OrderId}-{(request.Direction == "Outbound" ? "O" : "R")}" });
    }
}
