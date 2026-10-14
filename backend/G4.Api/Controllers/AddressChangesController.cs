using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;

namespace G4.Api.Controllers;
[ApiController]
[Route("api")]
public sealed class AddressChangesController(IAddressChangeService changes, IHostEnvironment environment) : ApiControllerBase(environment)
{
    [HttpPost("orders/{orderId:int}/address-changes")]
    public async Task<IActionResult> RequestChange(int orderId, AddressChangeInput input, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await changes.RequestAsync(orderId, CurrentUserId, input, ct));
    }

    [HttpPost("seller/address-changes/{id:int}/decision")]
    public async Task<IActionResult> Decide(int id, AddressChangeDecision input, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        return Ok(await changes.DecideAsync(id, CurrentUserId, input, ct));
    }

    [HttpPost("shipper/shipments/{id:int}/address-change/acknowledge")]
    public async Task<IActionResult> Acknowledge(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("shipper"))
            return StatusCode(403);
        await changes.AcknowledgeAsync(id, CurrentUserId, ct);
        return Ok(new { acknowledged = true });
    }
}
