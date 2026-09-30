
using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;

namespace G4.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class ShippingController(IShipmentService shipping, CarrierAvailabilityState carrierState,
    IHostEnvironment environment) : ApiControllerBase(environment)
{
    [HttpPost("seller/orders/{id:int}/ship")]
    public async Task<IActionResult> Ship(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        var shipment = await shipping.CreateOutboundAsync(id, ct);
        return Ok(new { shipment.Id, shipment.Status, shipment.TrackingNumber });
    }

    [HttpPost("carrier/fail-next")]
    public IActionResult FailNext(CarrierFailureRequest request)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        if (request.Direction is not ("Outbound" or "Return")) return BadRequest();
        carrierState.FailNext($"ship-{request.OrderId}-{request.Direction.ToLowerInvariant()}", request.Count);
        return Ok();
    }

    [HttpPost("shipments/{id:int}/events")]
    public async Task<IActionResult> AddEvent(int id, ShippingEventRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        var applied = await shipping.RecordEventAsync(id, request.EventId ?? Guid.NewGuid().ToString("N"),
            request.Status, request.Location, request.Note, ct);
        return Ok(new { applied });
    }
}
