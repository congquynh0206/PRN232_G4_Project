
using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class ShippingController(ApplicationDbContext db, IShipmentService shipping, CarrierAvailabilityState carrierState,
    IHostEnvironment environment) : ApiControllerBase(environment)
{
    [HttpPost("seller/orders/{id:int}/ship")]
    public async Task<IActionResult> Ship(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        if (!await db.OrderTables.AnyAsync(o => o.Id == id && o.SellerId == CurrentUserId, ct)) return StatusCode(403);
        var shipment = await shipping.CreateOutboundAsync(id, ct);
        return Ok(new { shipment.Id, shipment.Status, shipment.TrackingNumber });
    }

    [HttpPost("carrier/fail-next")]
    public IActionResult FailNext(CarrierFailureRequest request)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        if (!db.OrderTables.Any(o => o.Id == request.OrderId && o.SellerId == CurrentUserId)) return StatusCode(403);
        if (request.Direction is not ("Outbound" or "Return")) return BadRequest();
        carrierState.FailNext($"ship-{request.OrderId}-{request.Direction.ToLowerInvariant()}", request.Count);
        return Ok();
    }

    [HttpPost("shipments/{id:int}/events")]
    public async Task<IActionResult> AddEvent(int id, ShippingEventRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("shipper")) return StatusCode(403);
        if (!await db.ShippingInfos.AnyAsync(s => s.Id == id, ct)) return NotFound();
        var applied = await shipping.RecordEventAsync(id, request.EventId ?? Guid.NewGuid().ToString("N"),
            request.Status, request.Location, request.Note, ct);
        return Ok(new { applied });
    }

    [HttpGet("shipper/shipments")]
    public async Task<IActionResult> Shipments(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("shipper")) return StatusCode(403);
        return Ok(await db.OrderTables.AsNoTracking()
            .Where(o => db.ShippingInfos.Any(s => s.OrderId == o.Id))
            .OrderByDescending(o => o.Id)
            .Select(o => new
            {
                o.Id, o.OrderDate, o.Status, o.TotalPrice, o.Currency,
                shipments = db.ShippingInfos.Where(s => s.OrderId == o.Id)
                    .Select(s => new { s.Id, s.Direction, s.TrackingNumber, s.Status, s.DeliveryAttempts }).ToList()
            }).ToListAsync(ct));
    }
}
