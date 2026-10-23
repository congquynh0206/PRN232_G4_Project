using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Orders;

public static class SellerReturnList
{
    public static async Task<object> ReadAsync(
        ApplicationDbContext db,
        int sellerId,
        string filter,
        int page,
        int pageSize,
        CancellationToken ct,
        int receiptSeconds = 172800
    )
    {
        var query = db
            .ReturnRequests.AsNoTracking()
            .Where(r => db.OrderTables.Any(o => o.Id == r.OrderId && o.SellerId == sellerId))
            .Select(r => new
            {
                r.Id,
                r.OrderId,
                r.Status,
                r.ReasonCode,
                r.Reason,
                r.Description,
                r.CreatedAt,
                ConfirmationDueAt = r.ConfirmationDueAt
                    ?? db.ShippingInfos.Where(s =>
                            s.OrderId == r.OrderId
                            && s.Direction == "Return"
                            && s.Status == "Delivered"
                            && s.DeliveredAt != null
                        )
                        .Select(s => (DateTime?)s.DeliveredAt!.Value.AddSeconds(receiptSeconds))
                        .FirstOrDefault(),
                r.BuyerRefundAmount,
                r.ReturnShippingFee,
                r.ShippingPayer,
                r.PricingPolicyVersion,
                BuyerName = db
                    .OrderTables.Where(o => o.Id == r.OrderId)
                    .Select(o => o.Buyer == null ? null : o.Buyer.Username)
                    .FirstOrDefault(),
                ProductTitle = db
                    .OrderItems.Where(i => i.OrderId == r.OrderId)
                    .OrderBy(i => i.Id)
                    .Select(i => i.ProductTitleSnapshot)
                    .FirstOrDefault(),
                ShipmentStatus = db
                    .ShippingInfos.Where(s => s.OrderId == r.OrderId && s.Direction == "Return")
                    .Select(s => s.Status)
                    .FirstOrDefault(),
                Disputed = db.Disputes.Any(d =>
                    d.OrderId == r.OrderId && d.WorkflowEnabled && d.IsOpen
                ),
            });
        var counts = new Dictionary<string, int>
        {
            ["all"] = await query.CountAsync(ct),
            ["approval"] = await query.CountAsync(r => r.Status == "Requested", ct),
            ["shipping"] = await query.CountAsync(
                r =>
                    !r.Disputed
                    && (r.Status == "Approved" || r.Status == "ReturnShipping")
                    && r.ShipmentStatus != "Delivered",
                ct
            ),
            ["receipt"] = await query.CountAsync(
                r =>
                    !r.Disputed
                    && (r.Status == "Approved" || r.Status == "ReturnShipping")
                    && r.ShipmentStatus == "Delivered",
                ct
            ),
            ["refund"] = await query.CountAsync(
                r =>
                    !r.Disputed
                    && (
                        r.Status == "ReceivedBySeller"
                        || r.Status == "RefundPending"
                        || r.Status == "RefundFailed"
                    ),
                ct
            ),
            ["disputed"] = await query.CountAsync(r => r.Disputed, ct),
            ["closed"] = await query.CountAsync(
                r => r.Status == "Refunded" || r.Status == "Rejected" || r.Status == "Closed",
                ct
            ),
        };
        query = filter switch
        {
            "approval" => query.Where(r => r.Status == "Requested"),
            "shipping" => query.Where(r =>
                !r.Disputed
                && (r.Status == "Approved" || r.Status == "ReturnShipping")
                && r.ShipmentStatus != "Delivered"
            ),
            "receipt" => query.Where(r =>
                !r.Disputed
                && (r.Status == "Approved" || r.Status == "ReturnShipping")
                && r.ShipmentStatus == "Delivered"
            ),
            "refund" => query.Where(r =>
                !r.Disputed
                && (
                    r.Status == "ReceivedBySeller"
                    || r.Status == "RefundPending"
                    || r.Status == "RefundFailed"
                )
            ),
            "disputed" => query.Where(r => r.Disputed),
            "closed" => query.Where(r =>
                r.Status == "Refunded" || r.Status == "Rejected" || r.Status == "Closed"
            ),
            "all" => query,
            _ => throw new ArgumentException("Bộ lọc trả hàng không hợp lệ."),
        };
        var totalCount = await query.CountAsync(ct);
        pageSize = Math.Clamp(pageSize, 1, 50);
        // A request can commit while the count queries run. Keep the selected
        // group's badge aligned with its most recent count; subsequent events refresh the rest.
        counts[filter] = totalCount;
        page = Math.Clamp(page, 1, Math.Max(1, (totalCount + pageSize - 1) / pageSize));
        var items = await query
            .OrderByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return new
        {
            page,
            pageSize,
            totalCount,
            counts,
            items,
        };
    }
}
