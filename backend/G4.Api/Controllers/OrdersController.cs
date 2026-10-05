

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
        [FromQuery] string filter = "all", [FromQuery] bool needsAction = false, CancellationToken ct = default)
    {
        if (!IsAvailable) return NotFound();
        if (HasRole("seller"))
            return Ok(await G4.Infrastructure.Orders.SellerOrderList.ReadAsync(db, CurrentUserId,
                page, pageSize, filter, needsAction, ct));
        if (HasRole("buyer"))
            return Ok(await G4.Infrastructure.Orders.BuyerOrderList.ReadAsync(db, CurrentUserId,
                page, pageSize, filter, ct));
        return StatusCode(403);
    }

    [HttpGet("orders/{id:int}")]
    public async Task<IActionResult> Order(int id, CancellationToken ct)
    {
        if (!IsAvailable) return NotFound();
        var order = await db.OrderTables.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return NotFound();
        var canRead = HasRole("buyer") && order.BuyerId == CurrentUserId ||
            HasRole("seller") && order.SellerId == CurrentUserId ||
            HasRole("shipper") && await db.ShippingInfos.AnyAsync(s => s.OrderId == id && s.ShipperId == CurrentUserId, ct) ||
            HasRole("admin") && await db.Disputes.AnyAsync(d => d.OrderId == id && d.WorkflowEnabled && d.EscalatedAt != null, ct);
        if (!canRead) return StatusCode(403);
        if (HasRole("shipper"))
        {
            var assigned = await db.ShippingInfos.AsNoTracking().Where(s => s.OrderId == id && s.ShipperId == CurrentUserId)
                .Select(s => new
                {
                    s.Id,
                    s.Direction,
                    s.Status,
                    s.TrackingNumber,
                    s.DeliveryAttempts,
                    PickupAddressSnapshot = s.PickupAddressSnapshot ?? (s.Direction == "Return" ? order.AddressSnapshot : order.PickupAddressSnapshot),
                    DeliveryAddressSnapshot = s.DeliveryAddressSnapshot ?? (s.Direction == "Return" ? order.PickupAddressSnapshot : order.AddressSnapshot),
                    s.ShipperId
                }).ToListAsync(ct);
            var assignedIds = assigned.Select(s => s.Id).ToArray();
            var assignedEvents = await db.ShippingEvents.AsNoTracking().Where(e => assignedIds.Contains(e.ShippingInfoId))
                .OrderBy(e => e.OccurredAt).Select(e => new { e.ShippingInfoId, e.Status, e.Location, e.Note, e.OccurredAt }).ToListAsync(ct);
            return Ok(new
            {
                order.Id,
                order.Status,
                order.OrderDate,
                order.UpdatedAt,
                order.TotalWeightKg,
                shipments = assigned,
                events = assignedEvents
            });
        }
        var items = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == id)
            .Select(i => new
            {
                i.ProductId,
                i.ProductTitleSnapshot,
                i.UnitPrice,
                i.Quantity,
                i.UnitWeightKgSnapshot,
                originalTotal = (i.UnitPrice ?? 0) * (i.Quantity ?? 0),
                sellerDiscount = i.SellerDiscountSnapshot,
                platformDiscount = i.PlatformDiscountSnapshot,
                imageUrl = i.Product == null ? null : i.Product.Images
            }).ToListAsync(ct);
        var payments = await db.Payments.AsNoTracking().Where(p => p.OrderId == id)
            .Select(p => new { p.Id, p.Method, p.Status, p.Amount, p.CreatedAt, p.PaidAt, p.ErrorCode }).ToListAsync(ct);
        var shipments = await db.ShippingInfos.AsNoTracking().Where(s => s.OrderId == id)
            .Select(s => new
            {
                s.Id,
                s.Direction,
                s.TrackingNumber,
                s.Status,
                s.DeliveryAttempts,
                s.FailureReason,
                s.ShipperId,
                s.ClaimedAt,
                s.PickupAddressSnapshot,
                s.DeliveryAddressSnapshot
            }).ToListAsync(ct);
        var shipmentIds = shipments.Select(s => s.Id).ToArray();
        var events = await db.ShippingEvents.AsNoTracking().Where(e => shipmentIds.Contains(e.ShippingInfoId))
            .OrderBy(e => e.OccurredAt).Select(e => new { e.ShippingInfoId, e.Status, e.Location, e.Note, e.OccurredAt })
            .ToListAsync(ct);
        var returnRequest = await db.ReturnRequests.AsNoTracking().Where(r => r.OrderId == id)
            .Select(r => new { r.Id, r.Reason, r.Status, r.DecisionReason, r.CreatedAt, r.ReceivedAt, r.ConfirmationDueAt }).SingleOrDefaultAsync(ct);
        var refunds = await db.Refunds.AsNoTracking().Where(r => r.OrderId == id)
            .Select(r => new { r.Id, r.Amount, r.Status, r.Reason, r.CreatedAt, r.CompletedAt }).ToListAsync(ct);
        var settlement = await db.SellerSettlements.AsNoTracking().Where(s => s.OrderId == id)
            .Select(s => new
            {
                s.GrossAmount,
                s.PlatformFeeAmount,
                s.NetAmount,
                s.ProcessingAmount,
                s.RefundedAmount,
                s.FeeCreditAmount,
                s.Status,
                s.ReleaseAt,
                s.ReleasedAt
            }).SingleOrDefaultAsync(ct);
        var dispute = await db.Disputes.AsNoTracking().Where(d => d.OrderId == id && d.WorkflowEnabled)
            .OrderByDescending(d => d.Id).Select(d => new { d.Id, d.Status, d.IsOpen, d.Outcome }).FirstOrDefaultAsync(ct);
        var promotions = await db.OrderPromotionSnapshots.AsNoTracking().Where(x => x.OrderId == id).OrderBy(x => x.PromotionId)
            .Select(x => new { x.PromotionId, x.Name, x.Type, x.FundingSource, x.Code, x.Version, x.Amount }).ToListAsync(ct);
        return Ok(new
        {
            order.Id,
            order.Status,
            order.OrderDate,
            order.UpdatedAt,
            order.PaymentExpiresAt,
            order.AddressSnapshot,
            order.CancelDecisionReason,
            order.Subtotal,
            order.DiscountAmount,
            order.ShippingFee,
            order.TotalPrice,
            order.Currency,
            order.CouponCode,
            order.TotalWeightKg,
            order.PickupAddressSnapshot,
            shippingBase = order.PricingSchemaVersion == 1 ? order.ShippingBase : order.ShippingFee,
            order.ShippingDiscount,
            order.SellerGoodsDiscount,
            order.PlatformSubsidy,
            sellerGross = order.PricingSchemaVersion == 1 ? order.SellerGrossSnapshot : order.TotalPrice,
            promotions,
            items,
            payments,
            shipments,
            events,
            returnRequest,
            refunds,
            settlement,
            dispute
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
