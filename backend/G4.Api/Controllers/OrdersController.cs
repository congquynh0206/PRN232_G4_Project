

using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class OrdersController(ApplicationDbContext db, ICheckoutService checkout, IReturnService returns,
    IHostEnvironment environment) : ApiControllerBase(environment)
{
    [HttpGet("orders")]
    public async Task<IActionResult> Orders(CancellationToken ct)
    {
        if (!IsAvailable) return NotFound();
        var query = db.OrderTables.AsNoTracking();
        if (HasRole("buyer")) query = query.Where(o => o.BuyerId == CurrentUserId);
        else if (HasRole("seller")) query = query.Where(o => o.SellerId == CurrentUserId);
        else return StatusCode(403);
        return Ok(await query.OrderByDescending(o => o.Id)
            .Select(o => new { o.Id, o.OrderDate, o.Status, o.TotalPrice, o.Currency }).ToListAsync(ct));
    }

    [HttpGet("orders/page")]
    public async Task<IActionResult> OrdersPage([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery] string filter = "all", CancellationToken ct = default)
    {
        if (!IsAvailable) return NotFound();
        var query = db.OrderTables.AsNoTracking();
        if (HasRole("buyer")) query = query.Where(o => o.BuyerId == CurrentUserId);
        else if (HasRole("seller")) query = query.Where(o => o.SellerId == CurrentUserId);
        else return StatusCode(403);
        query = filter switch
        {
            "pending" => query.Where(o => o.Status == "AwaitingPayment" || o.Status == "Paid" || o.Status == "Preparing" || o.Status == "CancelRequested"),
            "shipping" => query.Where(o => o.Status == "Shipping"),
            "complete" => query.Where(o => o.Status == "Delivered" || o.Status == "Closed"),
            "cancelled" => query.Where(o => o.Status == "Cancelled" || o.Status == "Expired"),
            _ => query
        };
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var totalCount = await query.CountAsync(ct);
        page = Math.Min(page, Math.Max(1, (totalCount + pageSize - 1) / pageSize));
        var items = await query.OrderByDescending(o => o.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new { o.Id, o.OrderDate, o.UpdatedAt, o.Status, o.TotalPrice, o.Currency })
            .ToListAsync(ct);
        return Ok(new { page, pageSize, totalCount, items });
    }

    [HttpGet("orders/{id:int}")]
    public async Task<IActionResult> Order(int id, CancellationToken ct)
    {
        if (!IsAvailable) return NotFound();
        var order = await db.OrderTables.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return NotFound();
        var canRead = HasRole("buyer") && order.BuyerId == CurrentUserId ||
            HasRole("seller") && order.SellerId == CurrentUserId ||
            HasRole("shipper") && await db.ShippingInfos.AnyAsync(s => s.OrderId == id, ct);
        if (!canRead) return StatusCode(403);
        var items = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == id)
            .Select(i => new { i.ProductId, i.ProductTitleSnapshot, i.UnitPrice, i.Quantity }).ToListAsync(ct);
        var payments = await db.Payments.AsNoTracking().Where(p => p.OrderId == id)
            .Select(p => new { p.Id, p.Method, p.Status, p.Amount, p.CreatedAt, p.PaidAt, p.ErrorCode }).ToListAsync(ct);
        var shipments = await db.ShippingInfos.AsNoTracking().Where(s => s.OrderId == id)
            .Select(s => new { s.Id, s.Direction, s.TrackingNumber, s.Status, s.DeliveryAttempts, s.FailureReason }).ToListAsync(ct);
        var shipmentIds = shipments.Select(s => s.Id).ToArray();
        var events = await db.ShippingEvents.AsNoTracking().Where(e => shipmentIds.Contains(e.ShippingInfoId))
            .OrderBy(e => e.OccurredAt).Select(e => new { e.ShippingInfoId, e.Status, e.Location, e.Note, e.OccurredAt })
            .ToListAsync(ct);
        var returnRequest = await db.ReturnRequests.AsNoTracking().Where(r => r.OrderId == id)
            .Select(r => new { r.Id, r.Reason, r.Status, r.DecisionReason, r.CreatedAt, r.ReceivedAt }).SingleOrDefaultAsync(ct);
        var refunds = await db.Refunds.AsNoTracking().Where(r => r.OrderId == id)
            .Select(r => new { r.Id, r.Amount, r.Status, r.Reason, r.CreatedAt, r.CompletedAt }).ToListAsync(ct);
        var settlement = await db.SellerSettlements.AsNoTracking().Where(s => s.OrderId == id)
            .Select(s => new { s.GrossAmount, s.PlatformFeeAmount, s.NetAmount, s.ProcessingAmount, s.RefundedAmount,
                s.FeeCreditAmount, s.Status, s.ReleaseAt, s.ReleasedAt }).SingleOrDefaultAsync(ct);
        return Ok(new
        {
            order.Id, order.Status, order.OrderDate, order.UpdatedAt, order.PaymentExpiresAt, order.AddressSnapshot, order.CancelDecisionReason,
            order.Subtotal, order.DiscountAmount, order.ShippingFee, order.TotalPrice, order.Currency, order.CouponCode,
            items, payments, shipments, events, returnRequest, refunds, settlement
        });
    }

    [HttpPost("orders/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer")) return StatusCode(403);
        var order = await db.OrderTables.SingleAsync(o => o.Id == id, ct);
        if (order.BuyerId != CurrentUserId) return StatusCode(403);
        var result = order.Status == "AwaitingPayment"
            ? await checkout.CancelUnpaidAsync(id, ct) : await returns.RequestCancelAsync(id, ct);
        return Ok(new { result.Id, result.Status });
    }

    [HttpPost("seller/orders/{id:int}/prepare")]
    public async Task<IActionResult> Prepare(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        var order = await db.OrderTables.SingleAsync(o => o.Id == id, ct);
        if (order.SellerId != CurrentUserId) return StatusCode(403);
        order.Status = OrderState.Next(order.Status!, "Prepare");
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { order.Id, order.Status });
    }

    [HttpPost("seller/orders/{id:int}/cancel/decision")]
    public async Task<IActionResult> DecideCancel(int id, CancellationDecisionRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        if (!await db.OrderTables.AnyAsync(o => o.Id == id && o.SellerId == CurrentUserId, ct)) return StatusCode(403);
        var order = await returns.DecideCancelAsync(id, request.Approve, request.Reason, ct);
        return Ok(new { order.Id, order.Status });
    }
}
