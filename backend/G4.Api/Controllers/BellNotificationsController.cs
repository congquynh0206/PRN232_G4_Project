using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G4.Api.Controllers;

[ApiController]
[Authorize(Roles = "buyer,seller,shipper,admin")]
[Route("api/me/notifications")]
public sealed class BellNotificationsController(
    ApplicationDbContext db,
    IHostEnvironment environment
) : ApiControllerBase(environment)
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default
    ) => Ok(await new BellNotificationService(db).ListAsync(CurrentUserId, page, pageSize, ct));

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> Read(long id, CancellationToken ct) =>
        await new BellNotificationService(db).MarkReadAsync(CurrentUserId, id, ct)
            ? NoContent()
            : NotFound();

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(
        ReadAllNotificationsRequest request,
        CancellationToken ct
    )
    {
        if (request.ThroughId < 0)
            return BadRequest();
        await new BellNotificationService(db).MarkAllReadAsync(
            CurrentUserId,
            request.ThroughId,
            ct
        );
        return NoContent();
    }
}
