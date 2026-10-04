
using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class ReturnsController(ApplicationDbContext db, IReturnService returns, IDisputeService disputes,
    IHostEnvironment environment) : ApiControllerBase(environment)
{
    [HttpPost("orders/{id:int}/returns")]
    public async Task<IActionResult> RequestReturn(int id, ReasonRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer")) return StatusCode(403);
        if (!await db.OrderTables.AnyAsync(o => o.Id == id && o.BuyerId == CurrentUserId, ct)) return StatusCode(403);
        var result = await returns.RequestReturnAsync(id, request.Reason, ct);
        return Ok(new { result.Id, result.Status });
    }

    [HttpPost("seller/returns/{id:int}/approve")]
    public async Task<IActionResult> ApproveReturn(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        if (!await SellerOwnsReturnAsync(id, ct)) return StatusCode(403);
        var result = await returns.ApproveAsync(id, ct);
        return Ok(new { result.Id, result.Status });
    }

    [HttpPost("seller/returns/{id:int}/ship")]
    public async Task<IActionResult> RetryReturnShipment(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        if (!await SellerOwnsReturnAsync(id, ct)) return StatusCode(403);
        var result = await returns.RetryShipmentAsync(id, ct);
        return Ok(new { result.Id, result.Status });
    }

    [HttpPost("seller/returns/{id:int}/reject")]
    public async Task<IActionResult> RejectReturn(int id, ReasonRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        if (!await SellerOwnsReturnAsync(id, ct)) return StatusCode(403);
        var result = await returns.RejectAsync(id, request.Reason, ct);
        return Ok(new { result.Id, result.Status });
    }

    [HttpPost("seller/returns/{id:int}/receive")]
    public async Task<IActionResult> ReceiveReturn(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        if (!await SellerOwnsReturnAsync(id, ct)) return StatusCode(403);
        var result = await returns.MarkReceivedAsync(id, ct);
        return Ok(new { result.Id, result.Status });
    }

    private Task<bool> SellerOwnsReturnAsync(int returnId, CancellationToken ct) =>
        db.ReturnRequests.AnyAsync(r => r.Id == returnId &&
            db.OrderTables.Any(o => o.Id == r.OrderId && o.SellerId == CurrentUserId), ct);

    [HttpPost("seller/returns/{id:int}/issue")]
    public async Task<IActionResult> ReturnIssue(int id, DisputeEvidenceRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        var result = await disputes.ReportReturnIssueAsync(id, CurrentUserId, request, ct);
        return Ok(new { result.Id, result.Status });
    }
}
