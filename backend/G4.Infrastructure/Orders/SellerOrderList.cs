using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Orders;

public static class SellerOrderList
{
    public static async Task<SellerOrderListPage> ReadAsync(ApplicationDbContext db, int sellerId,
        int page, int pageSize, string filter, bool needsAction, CancellationToken ct = default)
    {
        // Project only list summaries; never fetch each order's detail to render a page.
        var query = db.OrderTables.AsNoTracking().Where(o => o.SellerId == sellerId).Select(o => new
        {
            o.Id, o.OrderDate, o.UpdatedAt, o.Status, o.TotalPrice, o.Currency,
            ProductTitle = db.OrderItems.Where(i => i.OrderId == o.Id).OrderBy(i => i.Id)
                .Select(i => i.ProductTitleSnapshot ?? i.Product!.Title).FirstOrDefault(),
            BuyerName = db.Users.Where(u => u.Id == o.BuyerId).Select(u => u.Username ?? u.Email).FirstOrDefault(),
            ProductCount = db.OrderItems.Count(i => i.OrderId == o.Id),
            ItemCount = db.OrderItems.Where(i => i.OrderId == o.Id).Sum(i => i.Quantity) ?? 0,
            HasRefund = db.Refunds.Any(r => r.OrderId == o.Id && r.Status == "Succeeded"),
            Attention = db.Refunds.Any(r => r.OrderId == o.Id && r.Status == "Succeeded") ? null
                : db.Disputes.Any(d => d.OrderId == o.Id && d.WorkflowEnabled && d.IsOpen && d.Status == "AwaitingSeller") ? "Cần phản hồi tranh chấp"
                : o.Status == "CancelRequested" ? "Chờ duyệt hủy"
                : db.ReturnRequests.Any(r => r.OrderId == o.Id && r.Status == "Requested") ? "Chờ duyệt trả hàng"
                : db.ShippingInfos.Any(s => s.OrderId == o.Id && s.Status == "ShipmentCreationFailed") ? "Cần tạo lại vận đơn"
                : db.ReturnRequests.Any(r => r.OrderId == o.Id && r.Status == "RefundFailed") ? "Hoàn tiền thất bại"
                : db.ReturnRequests.Any(r => r.OrderId == o.Id && (r.Status == "Approved" || r.Status == "ReturnShipping")) &&
                  db.ShippingInfos.Any(s => s.OrderId == o.Id && s.Direction == "Return" && s.Status == "Delivered") ? "Chờ xác nhận hàng trả"
                : db.ReturnRequests.Any(r => r.OrderId == o.Id && r.Status == "ReceivedBySeller") ? "Chờ hoàn tiền hàng trả"
                : db.ShippingInfos.Any(s => s.OrderId == o.Id && s.Direction == "Outbound" && s.Status == "ReturnedToSeller") ? "Chờ hoàn tiền chuyển hoàn"
                : o.Status == "Paid" ? "Chờ chuẩn bị hàng"
                : o.Status == "Preparing" && !db.ShippingInfos.Any(s => s.OrderId == o.Id && s.Direction == "Outbound") ? "Chờ tạo vận đơn" : null,
            Dispute = db.Disputes.Where(d => d.OrderId == o.Id && d.WorkflowEnabled).OrderByDescending(d => d.Id)
                .Select(d => new SellerOrderCaseBadge(d.Id, d.Status!, d.IsOpen, d.Outcome)).FirstOrDefault()
        });
        var actionCount = await query.CountAsync(o => o.Attention != null, ct);
        if (needsAction) query = query.Where(o => o.Attention != null);
        var statusCounts = await query.GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(params string[] statuses) => statusCounts.Where(g => statuses.Contains(g.Status)).Sum(g => g.Count);
        var counts = new Dictionary<string, int>
        {
            ["all"] = statusCounts.Sum(g => g.Count), ["unpaid"] = Count("AwaitingPayment"),
            ["ready"] = Count("Paid", "Preparing", "CancelRequested"), ["shipping"] = Count("Shipping"),
            ["complete"] = Count("Delivered", "Closed"), ["cancelled"] = Count("Cancelled", "Expired")
        };
        query = filter switch
        {
            "unpaid" => query.Where(o => o.Status == "AwaitingPayment"),
            "ready" => query.Where(o => o.Status == "Paid" || o.Status == "Preparing" || o.Status == "CancelRequested"),
            "shipping" => query.Where(o => o.Status == "Shipping"),
            "complete" => query.Where(o => o.Status == "Delivered" || o.Status == "Closed"),
            "cancelled" => query.Where(o => o.Status == "Cancelled" || o.Status == "Expired"),
            _ => query
        };
        var totalCount = await query.CountAsync(ct);
        pageSize = Math.Clamp(pageSize, 1, 50);
        page = Math.Clamp(page, 1, Math.Max(1, (totalCount + pageSize - 1) / pageSize));
        var rows = await query.OrderByDescending(o => o.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new SellerOrderListPage(page, pageSize, totalCount, counts, actionCount,
            rows.Select(o => new SellerOrderCard(o.Id, o.OrderDate, o.UpdatedAt, o.Status, o.TotalPrice,
                o.Currency, o.ProductTitle, o.BuyerName, o.ProductCount, o.ItemCount, o.HasRefund, o.Attention, o.Dispute)).ToList());
    }
}
