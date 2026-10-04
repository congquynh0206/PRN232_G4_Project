using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Orders;

public static class BuyerOrderList
{
    public static async Task<BuyerOrderListPage> ReadAsync(ApplicationDbContext db, int buyerId,
        int page, int pageSize, string filter, CancellationToken ct = default)
    {
        var query = db.OrderTables.AsNoTracking().Where(o => o.BuyerId == buyerId).Select(o => new
        {
            o.Id, o.OrderDate, o.UpdatedAt, o.Status, o.TotalPrice, o.Currency,
            ProductTitle = db.OrderItems.Where(i => i.OrderId == o.Id).OrderBy(i => i.Id)
                .Select(i => i.ProductTitleSnapshot ?? (i.Product == null ? null : i.Product.Title)).FirstOrDefault(),
            ImageUrl = db.OrderItems.Where(i => i.OrderId == o.Id).OrderBy(i => i.Id)
                .Select(i => i.Product == null ? null : i.Product.Images).FirstOrDefault(),
            SellerName = db.Users.Where(u => u.Id == o.SellerId).Select(u => u.Username ?? u.Email).FirstOrDefault(),
            ProductCount = db.OrderItems.Count(i => i.OrderId == o.Id),
            ItemCount = db.OrderItems.Where(i => i.OrderId == o.Id).Sum(i => i.Quantity) ?? 0,
            HasRefund = db.Refunds.Any(r => r.OrderId == o.Id && r.Status == "Succeeded"),
            Attention = db.Refunds.Any(r => r.OrderId == o.Id && r.Status == "Succeeded") ? null
                : db.Disputes.Any(d => d.OrderId == o.Id && d.WorkflowEnabled && d.IsOpen && d.Status == "AwaitingBuyer") ? "Cần phản hồi phương án"
                : db.ReturnRequests.Any(r => r.OrderId == o.Id && r.Status == "Requested") ? "Chờ người bán duyệt trả hàng"
                : db.ReturnRequests.Any(r => r.OrderId == o.Id && r.Status == "ReturnShipping") ? "Đang trả hàng về người bán"
                : o.Status == "CancelRequested" ? "Chờ người bán duyệt hủy" : null,
            Dispute = db.Disputes.Where(d => d.OrderId == o.Id && d.WorkflowEnabled).OrderByDescending(d => d.Id)
                .Select(d => new SellerOrderCaseBadge(d.Id, d.Status!, d.IsOpen, d.Outcome)).FirstOrDefault()
        });
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
            "pending" => query.Where(o => o.Status == "AwaitingPayment" || o.Status == "Paid" || o.Status == "Preparing" || o.Status == "CancelRequested"),
            "shipping" => query.Where(o => o.Status == "Shipping"),
            "complete" => query.Where(o => o.Status == "Delivered" || o.Status == "Closed"),
            "cancelled" => query.Where(o => o.Status == "Cancelled" || o.Status == "Expired"),
            _ => query
        };
        var totalCount = await query.CountAsync(ct);
        pageSize = Math.Clamp(pageSize, 1, 50);
        page = Math.Clamp(page, 1, Math.Max(1, (totalCount + pageSize - 1) / pageSize));
        var rows = await query.OrderByDescending(o => o.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new BuyerOrderListPage(page, pageSize, totalCount, counts,
            rows.Select(o => new BuyerOrderCard(o.Id, o.OrderDate, o.UpdatedAt, o.Status, o.TotalPrice,
                o.Currency, o.ProductTitle, o.ImageUrl, o.SellerName, o.ProductCount, o.ItemCount, o.HasRefund, o.Attention, o.Dispute)).ToList());
    }
}
