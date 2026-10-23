using System.Data;
using G4.Contracts.Checkout;
using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Finance;

public static class SellerFundsQuery
{
    public static async Task<SellerFundsPage> ReadAsync(
        ApplicationDbContext db,
        int sellerId,
        int page,
        int pageSize,
        CancellationToken ct = default,
        int? orderId = null,
        IConfiguration? config = null
    )
    {
        // Keep the settlement, current hold ledger and blocking cases in one read snapshot.
        await using var transaction =
            db.Database.IsRelational() && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                : null;
        var query = Pending(db, sellerId, orderId);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var totalCount = await query.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (totalCount + pageSize - 1) / pageSize));
        var rows = await query
            .OrderByDescending(x => x.Settlement.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var ids = rows.Select(x => x.Settlement.OrderId).ToArray();
        if (ids.Length == 0)
        {
            if (transaction != null)
                await transaction.CommitAsync(ct);
            return new(page, pageSize, totalCount, []);
        }
        var orders = await db
            .OrderTables.AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .Select(o => new { o.Id, o.Status })
            .ToDictionaryAsync(o => o.Id, ct);
        var disputes = await db
            .Disputes.AsNoTracking()
            .Where(d =>
                d.OrderId != null && ids.Contains(d.OrderId.Value) && d.WorkflowEnabled && d.IsOpen
            )
            .ToListAsync(ct);
        var returns = await db
            .ReturnRequests.AsNoTracking()
            .Where(r =>
                r.OrderId != null
                && ids.Contains(r.OrderId.Value)
                && r.Status != "Rejected"
                && r.Status != "Refunded"
                && r.Status != "Closed"
            )
            .ToListAsync(ct);
        var deliveredReturns = await db
            .ShippingInfos.AsNoTracking()
            .Where(s =>
                s.OrderId != null
                && ids.Contains(s.OrderId.Value)
                && s.Direction == "Return"
                && s.Status == "Delivered"
                && s.DeliveredAt != null
            )
            .Select(s => new { OrderId = s.OrderId!.Value, s.DeliveredAt })
            .ToDictionaryAsync(s => s.OrderId, s => s.DeliveredAt, ct);
        var receiptSeconds = Math.Max(
            1,
            config?.GetValue("Returns:ReceiptSeconds", 172800) ?? 172800
        );
        var agreementSeconds = Math.Max(
            1,
            config?.GetValue("Disputes:ReturnReceiptSeconds", 172800) ?? 172800
        );
        var items = rows.Select(row =>
            {
                var s = row.Settlement;
                var dispute = disputes
                    .OrderByDescending(d => d.Id)
                    .FirstOrDefault(d => d.OrderId == s.OrderId);
                var returned = returns
                    .OrderByDescending(r => r.Id)
                    .FirstOrDefault(r => r.OrderId == s.OrderId);
                var orderStatus = orders.GetValueOrDefault(s.OrderId)?.Status ?? "Unknown";
                var reason =
                    dispute != null ? "Dispute"
                    : returned != null ? "Return"
                    : row.Held > 0 || s.Status != "Processing" ? "Settlement"
                    : orderStatus is not ("Delivered" or "Closed") && s.ReleaseAt != null ? "Order"
                    : null;
                DateTime? responseDue =
                    dispute?.Status == "AwaitingSeller" ? dispute.SellerResponseDueAt
                    : dispute?.Status == "AwaitingBuyer" ? dispute.BuyerResponseDueAt
                    : dispute == null && returned?.Status is "Approved" or "ReturnShipping"
                        ? returned.ConfirmationDueAt
                    : null;
                var receiptPending = returned?.Status is "Approved" or "ReturnShipping";
                var agreement =
                    dispute?.Status == "ExecutingAgreement"
                    && dispute.Proposal == "ReturnRefund"
                    && receiptPending;
                if (
                    receiptPending && deliveredReturns.GetValueOrDefault(s.OrderId) is { } delivered
                )
                {
                    if (agreement)
                        responseDue = delivered.AddSeconds(agreementSeconds);
                    else if (dispute == null)
                        responseDue =
                            returned!.ConfirmationDueAt ?? delivered.AddSeconds(receiptSeconds);
                }
                var responseKind =
                    dispute?.Status == "AwaitingSeller" ? "seller-response"
                    : dispute?.Status == "AwaitingBuyer" ? "buyer-response"
                    : responseDue != null
                        ? agreement ? "return-agreement"
                            : "return-receipt"
                    : null;
                return new SellerFundView(
                    s.OrderId,
                    orderStatus,
                    s.Status,
                    s.Currency,
                    s.ProcessingAmount,
                    row.Held,
                    s.SellerLevelSnapshot,
                    s.HoldPolicyVersion,
                    s.HoldSecondsSnapshot,
                    s.HoldStartedAt,
                    s.ReleaseAt,
                    reason,
                    dispute?.Id,
                    returned?.Id,
                    responseDue,
                    responseKind
                );
            })
            .ToList();
        if (transaction != null)
            await transaction.CommitAsync(ct);
        return new(page, pageSize, totalCount, items);
    }

    public static IQueryable<SellerFundBalance> Pending(
        ApplicationDbContext db,
        int sellerId,
        int? orderId = null
    )
    {
        return from s in db.SellerSettlements.AsNoTracking()
            where s.SellerId == sellerId && (orderId == null || s.OrderId == orderId)
            let holdId = db.FinancialTransactions.Where(t =>
                    t.SettlementId == s.Id && t.Type == "FundHold"
                )
                .Select(t => (long?)t.Id)
                .Max()
                ?? 0
            let held = s.Status == "OnHold"
            || s.Status == "RefundPending"
            || s.Status == "PartiallyRefunded"
                ? (
                    db.FinancialTransactions.Where(t => t.SettlementId == s.Id && t.Id == holdId)
                        .Sum(t => (decimal?)t.Amount)
                    ?? 0
                )
                    + (
                        db.FinancialTransactions.Where(t =>
                                t.SettlementId == s.Id
                                && t.Id > holdId
                                && t.Type == "FundHoldConsumed"
                            )
                            .Sum(t => (decimal?)t.Amount)
                        ?? 0
                    )
                    - (
                        db.FinancialTransactions.Where(t =>
                                t.SettlementId == s.Id && t.Id > holdId && t.Type == "HoldReleased"
                            )
                            .Sum(t => (decimal?)t.Amount)
                        ?? 0
                    )
                : 0
            where s.ProcessingAmount > 0 || held > 0
            select new SellerFundBalance { Settlement = s, Held = held > 0 ? held : 0 };
    }
}

public sealed class SellerFundBalance
{
    public SellerSettlement Settlement { get; init; } = null!;
    public decimal Held { get; init; }
}
