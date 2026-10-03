using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class FinanceController(ApplicationDbContext db, ISellerFinanceService finance, IReturnService returns,
    IHostEnvironment environment) : ApiControllerBase(environment)
{
    [HttpGet("seller/finance")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        return Ok(await finance.GetSummaryAsync(CurrentUserId, ct));
    }

    [HttpGet("seller/finance/transactions/page")]
    public async Task<IActionResult> TransactionsPage([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var query = db.FinancialTransactions.AsNoTracking()
            .Where(x => x.Type != "RefundApplied" &&
                db.SellerAccounts.Any(a => a.Id == x.SellerAccountId && a.SellerId == CurrentUserId));
        var totalCount = await query.CountAsync(ct);
        page = Math.Min(page, Math.Max(1, (totalCount + pageSize - 1) / pageSize));
        var items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new FinanceTransactionView(x.Id, x.OrderId, x.Type, x.Bucket, x.Amount,
                x.Currency, x.Description, x.CreatedAt)).ToListAsync(ct);
        return Ok(new { page, pageSize, totalCount, items });
    }

    [HttpGet("seller/finance/payouts/page")]
    public async Task<IActionResult> PayoutsPage([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var query = db.SellerPayouts.AsNoTracking()
            .Where(x => db.SellerAccounts.Any(a => a.Id == x.SellerAccountId && a.SellerId == CurrentUserId));
        var totalCount = await query.CountAsync(ct);
        page = Math.Min(page, Math.Max(1, (totalCount + pageSize - 1) / pageSize));
        var items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new PayoutView(x.Id, x.Amount, x.Currency, x.Status, x.DestinationMasked,
                x.BankReferenceId, x.CreatedAt, x.CompletedAt)).ToListAsync(ct);
        return Ok(new { page, pageSize, totalCount, items });
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
        var status = await db.SellerSettlements.Where(x => x.OrderId == orderId).Select(x => x.Status).SingleAsync(ct);
        return Ok(new { orderId, status });
    }

    [HttpPost("orders/{orderId:int}/fund-hold/resolve")]
    public async Task<IActionResult> ResolveHold(int orderId, FundHoldResolutionRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("admin")) return StatusCode(403);
        await finance.ResolveHoldAsync(orderId, request.ReleaseToSeller, ct);
        if (request.ReleaseToSeller) return Ok(new { orderId, resolution = "SellerWins" });
        var refund = await returns.RefundAsync(orderId, "dispute", null, ct);
        return Ok(new
        {
            orderId,
            resolution = refund.Status == "Succeeded" ? "BuyerWins" : "RefundPending",
            refundStatus = refund.Status
        });
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
                amount = db.FinancialTransactions.Where(t => t.SettlementId == x.Id && t.Type == "FundHold")
                    .Select(t => t.Amount).FirstOrDefault(),
                reason = db.FinancialTransactions.Where(t => t.OrderId == x.OrderId && t.Type == "FundHold")
                    .OrderByDescending(t => t.Id).Select(t => t.Description).FirstOrDefault(),
                x.CreatedAt
            }).ToListAsync(ct));
    }

    [HttpGet("admin/fund-holds/page")]
    public async Task<IActionResult> FundHoldsPage([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (!IsAvailable || !HasRole("admin")) return StatusCode(403);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var query = db.SellerSettlements.AsNoTracking()
            .Where(x => x.Status == "OnHold" || x.Status == "RefundPending");
        var totalCount = await query.CountAsync(ct);
        page = Math.Min(page, Math.Max(1, (totalCount + pageSize - 1) / pageSize));
        var items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.OrderId, x.SellerId, x.Status,
                amount = db.FinancialTransactions.Where(t => t.SettlementId == x.Id && t.Type == "FundHold")
                    .Select(t => t.Amount).FirstOrDefault(),
                reason = db.FinancialTransactions.Where(t => t.OrderId == x.OrderId && t.Type == "FundHold")
                    .OrderByDescending(t => t.Id).Select(t => t.Description).FirstOrDefault(),
                x.CreatedAt
            }).ToListAsync(ct);
        return Ok(new { page, pageSize, totalCount, items });
    }
}
