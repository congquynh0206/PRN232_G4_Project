
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class NotificationsController(ApplicationDbContext db, IHostEnvironment environment) : ApiControllerBase(environment)
{
    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox(CancellationToken ct)
    {
        if (!IsAvailable) return NotFound();
        if (!HasAnyRole()) return StatusCode(403);
        return Ok(await db.NotificationOutbox.AsNoTracking().OrderByDescending(n => n.Id)
            .Select(n => new { n.Id, n.OrderId, n.EventType, n.Recipient, n.Subject, n.Body, n.Status, n.CreatedAt })
            .ToListAsync(ct));
    }
}
