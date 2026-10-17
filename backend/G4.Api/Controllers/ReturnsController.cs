using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Controllers;
[ApiController]
[Route("api")]
public sealed class ReturnsController(
    ApplicationDbContext db,
    IReturnService returns,
    IDisputeService disputes,
    CarrierAvailabilityState carrierState,
    IHostEnvironment environment
) : ApiControllerBase(environment)
{
    [HttpGet("seller/returns/page")]
    public async Task<IActionResult> ListReturns(string filter = "approval", int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        return Ok(await G4.Infrastructure.Orders.SellerReturnList.ReadAsync(db, CurrentUserId, filter, page, pageSize, ct));
    }
    [HttpPost("orders/{id:int}/returns")]
    public async Task<IActionResult> RequestReturn(int id, ReturnRequestInput request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        if (!await db.OrderTables.AnyAsync(o => o.Id == id && o.BuyerId == CurrentUserId, ct))
            return StatusCode(403);
        var result = await returns.RequestPricedReturnAsync(id, request, ct);
        return Ok(new { result.Id, result.Status });
    }

    [HttpGet("orders/{id:int}/returns/quote")]
    public async Task<IActionResult> QuoteReturn(int id, string reasonCode, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer")) return StatusCode(403);
        if (!await db.OrderTables.AnyAsync(x => x.Id == id && x.BuyerId == CurrentUserId, ct)) return StatusCode(403);
        return Ok(await returns.QuoteAsync(id, reasonCode, ct));
    }

    [HttpPost("seller/returns/{id:int}/approve")]
    public async Task<IActionResult> ApproveReturn(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        if (!await SellerOwnsReturnAsync(id, ct))
            return StatusCode(403);
        var result = await returns.ApproveAsync(id, ct);
        var shipmentStatus = await db.ShippingInfos.Where(s => s.OrderId == result.OrderId && s.Direction == "Return").Select(s => s.Status).SingleOrDefaultAsync(ct);
        return Ok(new { result.Id, result.Status, shipmentStatus });
    }

    [HttpPost("seller/returns/{id:int}/ship")]
    public async Task<IActionResult> RetryReturnShipment(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        if (!await SellerOwnsReturnAsync(id, ct))
            return StatusCode(403);
        var result = await returns.RetryShipmentAsync(id, ct);
        var shipmentStatus = await db.ShippingInfos.Where(s => s.OrderId == result.OrderId && s.Direction == "Return").Select(s => s.Status).SingleOrDefaultAsync(ct);
        return Ok(new { result.Id, result.Status, shipmentStatus });
    }

    [HttpPost("seller/returns/{id:int}/approve/simulate-failure")]
    public async Task<IActionResult> ApproveWithCarrierFailure(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        if (!await SellerOwnsReturnAsync(id, ct)) return StatusCode(403);
        var orderId = (await db.ReturnRequests.AsNoTracking().SingleAsync(r => r.Id == id, ct)).OrderId!.Value;
        await using var tx = await G4.Infrastructure.Promotions.PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        if (await db.ShippingInfos.AnyAsync(s => s.OrderId == orderId && s.Direction == "Return" && s.TrackingNumber != null, ct))
            throw new InvalidOperationException("Đã có vận đơn trả hợp lệ.");
        carrierState.FailNext($"ship-{orderId}-return", 3);
        try
        {
            var result = await returns.ApproveAsync(id, ct);
            if (tx != null) await tx.CommitAsync(ct);
            var shipmentStatus = await db.ShippingInfos.Where(s => s.OrderId == orderId && s.Direction == "Return").Select(s => s.Status).SingleOrDefaultAsync(ct);
            return Ok(new { result.Id, result.Status, shipmentStatus });
        }
        finally { carrierState.FailNext($"ship-{orderId}-return", 0); }
    }

    [HttpPost("seller/returns/{id:int}/reject")]
    public async Task<IActionResult> RejectReturn(int id, ReasonRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        if (!await SellerOwnsReturnAsync(id, ct))
            return StatusCode(403);
        var result = await returns.RejectAsync(id, request.Reason, ct);
        return Ok(new { result.Id, result.Status });
    }

    [HttpPost("seller/returns/{id:int}/receive")]
    public async Task<IActionResult> ReceiveReturn(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        if (!await SellerOwnsReturnAsync(id, ct))
            return StatusCode(403);
        var result = await returns.MarkReceivedAsync(id, ct);
        return Ok(new { result.Id, result.Status });
    }

    private Task<bool> SellerOwnsReturnAsync(int returnId, CancellationToken ct) => db.ReturnRequests.AnyAsync(
        r => r.Id == returnId && db.OrderTables.Any(o => o.Id == r.OrderId && o.SellerId == CurrentUserId),
        ct
    );
    [HttpPost("seller/returns/{id:int}/issue")]
    public async Task<IActionResult> ReturnIssue(int id, DisputeEvidenceRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        var result = await disputes.ReportReturnIssueAsync(id, CurrentUserId, request, ct);
        return Ok(new { result.Id, result.Status });
    }
}
