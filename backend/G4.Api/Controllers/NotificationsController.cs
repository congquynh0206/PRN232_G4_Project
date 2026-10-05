
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using G4.Contracts.Checkout;
using G4.Infrastructure.Diagnostics;

namespace G4.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class NotificationsController(ApplicationDbContext db, IHostEnvironment environment, NotificationProcessor processor) : ApiControllerBase(environment)
{
    private string ActorRole => HasRole("admin") ? "admin" : HasRole("seller") ? "seller" : HasRole("buyer") ? "buyer" : "shipper";

    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications([FromQuery] NotificationQuery query, CancellationToken ct)
    {
        if (!IsAvailable) return NotFound();
        return Ok(await new DiagnosticsQueryService(db).NotificationsAsync(CurrentUserId, ActorRole, query, ct));
    }

    [HttpGet("notifications/{id:int}")]
    public async Task<IActionResult> Notification(int id, CancellationToken ct)
    {
        if (!IsAvailable) return NotFound();
        return Ok(await new DiagnosticsQueryService(db).NotificationAsync(id, CurrentUserId, ActorRole, ct));
    }

    [HttpPost("admin/notifications/{id:int}/retry")]
    public async Task<IActionResult> Retry(int id, CancellationToken ct)
    {
        if (!IsAvailable) return NotFound();
        if (!HasRole("admin")) return StatusCode(403);
        if (!await db.NotificationOutbox.AnyAsync(x=>x.Id==id,ct)) return NotFound();
        if (!await processor.RetryFailedAsync(id,ct)) return Conflict(new { error="Chỉ gửi lại được email đang ở trạng thái lỗi." });
        return Accepted(new { id, status="Pending" });
    }

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
