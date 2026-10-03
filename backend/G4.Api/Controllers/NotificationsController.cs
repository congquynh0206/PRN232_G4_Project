
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
        if (!HasRole("admin")) return StatusCode(403);
        return Ok(await db.NotificationOutbox.AsNoTracking().OrderByDescending(n => n.Id)
            .Select(n => new { n.Id, n.OrderId, n.EventType, n.Recipient, n.Subject, n.Body, n.Status, n.CreatedAt })
            .ToListAsync(ct));
    }

    [HttpGet("inbox/page")]
    public async Task<IActionResult> InboxPage([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (!IsAvailable) return NotFound();
        if (!HasRole("admin")) return StatusCode(403);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var query = db.NotificationOutbox.AsNoTracking();
        var totalCount = await query.CountAsync(ct);
        page = Math.Min(page, Math.Max(1, (totalCount + pageSize - 1) / pageSize));
        var items = await query.OrderByDescending(n => n.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(n => new { n.Id, n.OrderId, n.EventType, n.Recipient, n.Subject, n.Body, n.Status, n.CreatedAt })
            .ToListAsync(ct);
        return Ok(new { page, pageSize, totalCount, items });
    }
}
