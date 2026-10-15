using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Controllers;
[ApiController]
[Route("api")]
public sealed class SellerDebtController(ApplicationDbContext db, ISellerFinanceService finance,
    IHostEnvironment environment, TimeProvider clock) : ApiControllerBase(environment)
{
    [HttpGet("seller/debt")]
    public async Task<IActionResult> Debt(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        return Ok(await finance.GetDebtAsync(CurrentUserId, ct));
    }

    [HttpPost("seller/debt/repayments")]
    public async Task<IActionResult> Repay(DebtRepaymentRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        return Ok(await finance.RepayDebtAsync(CurrentUserId, request, ct));
    }

    [HttpGet("admin/seller-debts")]
    public async Task<IActionResult> Sellers([FromQuery] string state = "debt", [FromQuery] int? sellerId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
    {
        if (!IsAvailable || !HasRole("admin")) return StatusCode(403);
        if (state is not ("all" or "debt" or "overdue")) return BadRequest(new { error = "Bộ lọc nợ chưa hợp lệ." });
        await finance.MaintainDebtsAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var query = db.SellerAccounts.AsNoTracking();
        if (state != "all") query = query.Where(a => a.NegativeBalance > 0);
        if (state == "overdue") query = query.Where(a => a.DebtDueAt <= now);
        if (sellerId != null) query = query.Where(a => a.SellerId == sellerId);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var totalCount = await query.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (totalCount + pageSize - 1) / pageSize));
        var items = await query.OrderBy(a => a.DebtDueAt).ThenBy(a => a.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new { a.SellerId, a.Status, a.NegativeBalance, a.DebtCycleNumber, a.DebtStartedAt, a.DebtDueAt,
                a.DebtSuspended, overdue = a.NegativeBalance > 0 && a.DebtDueAt <= now,
                email = db.Users.Where(u => u.Id == a.SellerId).Select(u => u.Email).FirstOrDefault() }).ToListAsync(ct);
        return Ok(new { page, pageSize, totalCount, items });
    }

    [HttpGet("admin/seller-debts/{sellerId:int}")]
    public async Task<IActionResult> Seller(int sellerId, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("admin")) return StatusCode(403);
        if (!await db.Users.AnyAsync(u => u.Id == sellerId && u.Role == "seller", ct)) return NotFound();
        return Ok(await finance.GetDebtAsync(sellerId, ct));
    }

    [HttpGet("seller/debt/history")]
    public Task<IActionResult> OwnHistory([FromQuery] string kind = "repayments", [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10, CancellationToken ct = default) => History(CurrentUserId, kind, page, pageSize, false, ct);

    [HttpGet("admin/seller-debts/{sellerId:int}/history")]
    public Task<IActionResult> SellerHistory(int sellerId, [FromQuery] string kind = "repayments", [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10, CancellationToken ct = default) => History(sellerId, kind, page, pageSize, true, ct);

    private async Task<IActionResult> History(int sellerId, string kind, int page, int pageSize, bool admin, CancellationToken ct)
    {
        if (!IsAvailable || !(admin ? HasRole("admin") : HasRole("seller"))) return StatusCode(403);
        var accountId = await db.SellerAccounts.Where(a => a.SellerId == sellerId).Select(a => (int?)a.Id).SingleOrDefaultAsync(ct);
        pageSize = Math.Clamp(pageSize, 1, 50);
        switch (kind)
        {
            case "repayments":
                return await HistoryPage(db.SellerDebtRepayments.AsNoTracking().Where(x => x.SellerAccountId == accountId)
                    .OrderByDescending(x => x.Id).Select(x => new DebtRepaymentView(x.Id, x.CycleNumber, x.Amount,
                        x.RemainingDebt, x.Currency, x.Status, x.BankReferenceId, x.CreatedAt)), page, pageSize, ct);
            case "events":
                return await HistoryPage(db.SellerDebtEvents.AsNoTracking().Where(x => x.SellerAccountId == accountId)
                    .OrderByDescending(x => x.Id), page, pageSize, ct);
            case "cycles":
                return await HistoryPage(db.SellerDebtCycles.AsNoTracking().Where(x => x.SellerAccountId == accountId)
                    .OrderByDescending(x => x.Id), page, pageSize, ct);
            case "transactions":
                return await HistoryPage(db.FinancialTransactions.AsNoTracking().Where(x => x.SellerAccountId == accountId && x.Type != "RefundApplied")
                    .OrderByDescending(x => x.Id).Select(x => new FinanceTransactionView(x.Id, x.OrderId, x.Type,
                        x.Bucket, x.Amount, x.Currency, x.Description, x.CreatedAt)), page, pageSize, ct);
            default: return BadRequest(new { error = "Loại lịch sử chưa hợp lệ." });
        }
    }

    private async Task<IActionResult> HistoryPage<T>(IQueryable<T> query, int page, int pageSize, CancellationToken ct)
    {
        var totalCount = await query.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (totalCount + pageSize - 1) / pageSize));
        return Ok(new { page, pageSize, totalCount, items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct) });
    }
}
