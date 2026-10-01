using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class FinanceController(ApplicationDbContext db, ISellerFinanceService finance,
    IHostEnvironment environment) : ApiControllerBase(environment)
{
    [HttpGet("seller/finance")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        return Ok(await finance.GetSummaryAsync(CurrentUserId, ct));
    }

    [HttpPost("seller/finance/evaluate-level")]
    public async Task<IActionResult> EvaluateLevel(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        return Ok(await finance.EvaluateLevelAsync(CurrentUserId, ct));
    }

    [HttpPost("seller/payouts")]
    public async Task<IActionResult> RequestPayout(PayoutRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        var payout = await finance.RequestPayoutAsync(CurrentUserId, request.Amount, request.Key,
            request.SimulateFailure, ct);
        return Ok(new PayoutView(payout.Id, payout.Amount, payout.Currency, payout.Status, payout.DestinationMasked,
            payout.BankReferenceId, payout.CreatedAt, payout.CompletedAt));
    }

    [HttpPost("orders/{orderId:int}/fund-hold")]
    public async Task<IActionResult> PlaceHold(int orderId, FundHoldRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer")) return StatusCode(403);
        if (!await db.OrderTables.AnyAsync(o => o.Id == orderId && o.BuyerId == CurrentUserId, ct)) return StatusCode(403);
        if (string.IsNullOrWhiteSpace(request.Reason)) return BadRequest("Reason is required");
        await finance.PlaceHoldAsync(orderId, request.Reason, ct);
        return Ok(new { orderId, status = "OnHold" });
    }

    [HttpPost("orders/{orderId:int}/fund-hold/resolve")]
    public async Task<IActionResult> ResolveHold(int orderId, FundHoldResolutionRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("admin")) return StatusCode(403);
        await finance.ResolveHoldAsync(orderId, request.ReleaseToSeller, ct);
        return Ok(new { orderId, resolution = request.ReleaseToSeller ? "SellerWins" : "BuyerWins" });
    }

    [HttpGet("admin/fund-holds")]
    public async Task<IActionResult> FundHolds(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("admin")) return StatusCode(403);
        return Ok(await db.SellerSettlements.AsNoTracking()
            .Where(x => x.Status == "OnHold" || x.Status == "RefundPending")
            .OrderByDescending(x => x.Id)
            .Select(x => new
            {
                x.OrderId, x.SellerId, x.Status,
                amount = x.NetAmount - (x.RefundedAmount - x.FeeCreditAmount),
                reason = db.FinancialTransactions.Where(t => t.OrderId == x.OrderId && t.Type == "FundHold")
                    .OrderByDescending(t => t.Id).Select(t => t.Description).FirstOrDefault(),
                x.CreatedAt
            }).ToListAsync(ct));
    }
}
