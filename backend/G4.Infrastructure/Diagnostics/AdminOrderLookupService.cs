using G4.Application.Services;
using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Diagnostics;

public sealed class AdminOrderLookupService(ApplicationDbContext db) : IAdminOrderLookupService
{
    private static readonly string[] QuickFilters = ["AwaitingPayment", "PaymentFailed", "HasRefund", "HasDispute"];

    public async Task<AdminOrderLookupPage> SearchAsync(AdminOrderLookupQuery options, CancellationToken ct = default)
    {
        if (options.Page < 1 || options.PageSize is < 1 or > 100)
            throw new ArgumentException("Trang hoặc số dòng mỗi trang chưa hợp lệ.");
        if (!string.IsNullOrWhiteSpace(options.QuickFilter) && !QuickFilters.Contains(options.QuickFilter))
            throw new ArgumentException("Bộ lọc nhanh chưa hợp lệ.");
        if (options.From is not null && options.To is not null && options.From >= options.To)
            throw new ArgumentException("Khoảng thời gian chưa hợp lệ.");

        var query = db.OrderTables.AsNoTracking().AsQueryable();
        if (options.OrderId is not null) query = query.Where(o => o.Id == options.OrderId);
        query = ApplySearch(query, options.Search, options.SearchType);
        if (!string.IsNullOrWhiteSpace(options.Buyer))
        {
            var buyer = options.Buyer.Trim();
            var buyerId = int.TryParse(buyer, out var parsedBuyer) ? parsedBuyer : (int?)null;
            query = query.Where(o => db.Users.Any(u => u.Id == o.BuyerId &&
                (buyerId != null && u.Id == buyerId || (u.Username != null && u.Username.Contains(buyer)) || (u.Email != null && u.Email.Contains(buyer)))));
        }
        if (!string.IsNullOrWhiteSpace(options.Seller))
        {
            var seller = options.Seller.Trim();
            var sellerId = int.TryParse(seller, out var parsedSeller) ? parsedSeller : (int?)null;
            query = query.Where(o => db.Users.Any(u => u.Id == o.SellerId &&
                (sellerId != null && u.Id == sellerId || (u.Username != null && u.Username.Contains(seller)) || (u.Email != null && u.Email.Contains(seller)))) ||
                o.SellerId == null && db.OrderItems.Any(i => i.OrderId == o.Id && i.Product != null && i.Product.Seller != null &&
                    (sellerId != null && i.Product.SellerId == sellerId || (i.Product.Seller.Username != null && i.Product.Seller.Username.Contains(seller)) || (i.Product.Seller.Email != null && i.Product.Seller.Email.Contains(seller)))));
        }
        if (options.From is not null) query = query.Where(o => o.OrderDate >= options.From);
        if (options.To is not null) query = query.Where(o => o.OrderDate <= options.To);
        if (!string.IsNullOrWhiteSpace(options.Status)) query = query.Where(o => o.Status == options.Status);
        if (!string.IsNullOrWhiteSpace(options.PaymentStatus)) query = options.PaymentStatus switch
        {
            "Succeeded" => query.Where(o => db.Payments.Any(p => p.OrderId == o.Id && p.Status == "Succeeded")),
            "Failed" => query.Where(o => db.Payments.Any(p => p.OrderId == o.Id && p.Status == "Failed")),
            "Pending" => query.Where(o => db.Payments.Any(p => p.OrderId == o.Id && p.Status == "Pending")),
            "Verifying" => query.Where(o => db.Payments.Any(p => p.OrderId == o.Id && p.Status == "Verifying")),
            "NotAttempted" => query.Where(o => !db.Payments.Any(p => p.OrderId == o.Id)),
            _ => throw new ArgumentException("Trạng thái payment chưa hợp lệ.")
        };
        if (!string.IsNullOrWhiteSpace(options.RefundStatus)) query = options.RefundStatus switch
        {
            "Pending" => query.Where(o => db.Refunds.Any(r => r.OrderId == o.Id && r.Status == "Pending")),
            "Succeeded" => query.Where(o => db.Refunds.Any(r => r.OrderId == o.Id && r.Status == "Succeeded")),
            "Failed" => query.Where(o => db.Refunds.Any(r => r.OrderId == o.Id && r.Status == "Failed")),
            _ => throw new ArgumentException("Trạng thái refund chưa hợp lệ.")
        };

        query = options.QuickFilter switch
        {
            "AwaitingPayment" => query.Where(o => o.Status == "AwaitingPayment" && !db.Payments.Any(p => p.OrderId == o.Id && p.Status == "Succeeded")),
            "PaymentFailed" => query.Where(o => db.Payments.Any(p => p.OrderId == o.Id && p.Status == "Failed")),
            "HasRefund" => query.Where(o => db.Refunds.Any(r => r.OrderId == o.Id)),
            "HasDispute" => query.Where(o => db.Disputes.Any(d => d.OrderId == o.Id)),
            _ => query
        };

        var count = await query.CountAsync(ct);
        var page = Math.Min(options.Page, Math.Max(1, (count + options.PageSize - 1) / options.PageSize));
        var orders = await query.OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Id)
            .Skip((page - 1) * options.PageSize).Take(options.PageSize)
            .Select(o => new
            {
                o.Id, o.OrderDate, o.TotalPrice, o.Status,
                BuyerName = db.Users.Where(u => u.Id == o.BuyerId).Select(u => u.Username).FirstOrDefault(),
                BuyerEmail = db.Users.Where(u => u.Id == o.BuyerId).Select(u => u.Email).FirstOrDefault(),
                SellerId = o.SellerId ?? db.OrderItems.Where(i => i.OrderId == o.Id && i.Product != null).Select(i => i.Product!.SellerId).FirstOrDefault(),
                Shipping = db.ShippingInfos.Where(s => s.OrderId == o.Id).OrderByDescending(s => s.Direction == "Outbound").ThenByDescending(s => s.Id).Select(s => s.Status).FirstOrDefault(),
                HasDispute = db.Disputes.Any(d => d.OrderId == o.Id),
                HasSuccess = db.Payments.Any(p => p.OrderId == o.Id && p.Status == "Succeeded"),
                HasPending = db.Payments.Any(p => p.OrderId == o.Id && p.Status == "Pending"),
                HasVerifying = db.Payments.Any(p => p.OrderId == o.Id && p.Status == "Verifying"),
                HasFailed = db.Payments.Any(p => p.OrderId == o.Id && p.Status == "Failed"),
                RefundedAmount = db.Refunds.Where(r => r.OrderId == o.Id && r.Status == "Succeeded").Sum(r => (decimal?)r.Amount) ?? 0m,
                HasRefund = db.Refunds.Any(r => r.OrderId == o.Id),
                RefundPending = db.Refunds.Any(r => r.OrderId == o.Id && r.Status == "Pending"),
                RefundSucceeded = db.Refunds.Any(r => r.OrderId == o.Id && r.Status == "Succeeded"),
                RefundFailed = db.Refunds.Any(r => r.OrderId == o.Id && r.Status == "Failed")
            }).ToListAsync(ct);

        var sellerIds = orders.Where(x => x.SellerId != null).Select(x => x.SellerId!.Value).Distinct().ToArray();
        var sellers = await db.Users.AsNoTracking().Where(x => sellerIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Username, x.Email }).ToDictionaryAsync(x => x.Id, ct);
        var items = orders.Select(x => new AdminOrderLookupRow(
            x.Id, UtcOrNull(x.OrderDate), MaskPerson(x.BuyerName, x.BuyerEmail),
            x.SellerId is int sellerId && sellers.TryGetValue(sellerId, out var seller) ? MaskPerson(seller.Username, seller.Email) : null,
            x.TotalPrice ?? 0m, x.Status, PaymentLabel(x.HasSuccess, x.HasPending, x.HasVerifying, x.HasFailed),
            RefundLabel(x.RefundPending, x.RefundSucceeded, x.RefundFailed), x.RefundedAmount, x.Shipping, x.HasFailed, x.HasRefund, x.HasDispute)).ToArray();
        return new(page, options.PageSize, count, items);
    }

    public async Task<AdminOrderLookupDetail> DetailAsync(int orderId, CancellationToken ct = default)
    {
        if (orderId <= 0) throw new ArgumentException("Mã đơn chưa hợp lệ.");
        var page = await SearchAsync(new AdminOrderLookupQuery(PageSize: 1, OrderId: orderId), ct);
        var order = page.Items.FirstOrDefault() ?? throw new KeyNotFoundException("Không tìm thấy đơn hàng.");
        var payments = await db.Payments.AsNoTracking().Where(p => p.OrderId == orderId).OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Select(p => new AdminOrderPaymentView(p.Id, p.Method, p.Status, p.Amount ?? 0m, p.CreatedAt, p.PaidAt,
                MaskReference(p.ProviderTransactionId), MaskReference(p.ProviderOrderId), p.ErrorCode)).ToListAsync(ct);
        var refunds = await db.Refunds.AsNoTracking().Where(r => r.OrderId == orderId).OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Select(r => new AdminOrderRefundView(r.Id, r.PaymentId, r.Amount, r.Currency, r.Reason, r.Status, r.CreatedAt, r.CompletedAt,
                MaskReference(r.ProviderRefundId))).ToListAsync(ct);
        var shipments = await db.ShippingInfos.AsNoTracking().Where(s => s.OrderId == orderId).OrderBy(s => s.Direction).ThenBy(s => s.CreatedAt)
            .Select(s => new
            {
                s.Id, s.Direction, s.Carrier, s.TrackingNumber, s.Status, s.CreatedAt, s.DeliveredAt,
                Events = db.ShippingEvents.Where(e => e.ShippingInfoId == s.Id).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
                    .Select(e => new AdminOrderShipmentEventView(e.Status, e.OccurredAt)).ToList()
            }).ToListAsync(ct);
        var disputes = await db.Disputes.AsNoTracking().Where(d => d.OrderId == orderId && d.EscalatedAt != null)
            .OrderByDescending(d => d.CreatedAt).Select(d => new AdminOrderDisputeView(d.Id, d.Status, d.CreatedAt)).ToListAsync(ct);
        var lineItems = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == orderId)
            .Select(i => new AdminOrderItemView(i.ProductTitleSnapshot ?? i.Product!.Title, i.Quantity ?? 0, i.UnitPrice ?? 0m)).ToListAsync(ct);
        var collected = await db.Payments.AsNoTracking().Where(p => p.OrderId == orderId && p.Status == "Succeeded").SumAsync(p => (decimal?)(p.Amount ?? 0m), ct) ?? 0m;
        var refunded = await db.Refunds.AsNoTracking().Where(r => r.OrderId == orderId && r.Status == "Succeeded").SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;
        return new(order, collected, refunded, Math.Max(0m, collected - refunded), lineItems,
            payments.Select(p => p with { CreatedAt = Utc(p.CreatedAt), PaidAt = UtcOrNull(p.PaidAt) }).ToArray(),
            refunds.Select(r => r with { CreatedAt = Utc(r.CreatedAt), CompletedAt = UtcOrNull(r.CompletedAt) }).ToArray(),
            shipments.Select(s => new AdminOrderShipmentView(s.Id, s.Direction, s.Carrier, MaskReference(s.TrackingNumber), s.Status,
                Utc(s.CreatedAt), UtcOrNull(s.DeliveredAt), s.Events.Select(e => e with { OccurredAt = Utc(e.OccurredAt) }).ToArray())).ToArray(),
            disputes.Select(d => d with { CreatedAt = Utc(d.CreatedAt) }).ToArray());
    }

    private IQueryable<OrderTable> ApplySearch(IQueryable<OrderTable> query, string? search, string? searchType)
    {
        var type = string.IsNullOrWhiteSpace(searchType) ? "auto" : searchType.Trim().ToLowerInvariant();
        if (type is not ("auto" or "order" or "payment" or "refund" or "provider"))
            throw new ArgumentException("Loại mã tìm kiếm chưa hợp lệ.");
        var term = search?.Trim();
        if (string.IsNullOrEmpty(term)) return query;
        if (term.Length > 100) throw new ArgumentException("Mã tìm kiếm tối đa 100 ký tự.");

        string? prefix = null;
        if (term.StartsWith('#')) { prefix = "order"; term = term[1..].Trim(); }
        else
        {
            foreach (var candidate in new[] { "order", "payment", "refund" })
                if (term.StartsWith(candidate + ":", StringComparison.OrdinalIgnoreCase))
                { prefix = candidate; term = term[(candidate.Length + 1)..].Trim(); break; }
        }
        if (prefix is not null)
        {
            if (type != "auto" && type != prefix)
                throw new ArgumentException("Loại mã đã chọn không khớp mã tìm kiếm.");
            type = prefix;
        }
        if (type == "auto") type = int.TryParse(term, out _) ? "order" : "provider";
        if (type == "provider")
            return query.Where(o =>
                db.Payments.Any(p => p.OrderId == o.Id && (p.ProviderTransactionId == term || p.ProviderOrderId == term)) ||
                db.Refunds.Any(r => r.OrderId == o.Id && r.ProviderRefundId == term) ||
                db.IntegrationLogs.Any(l => l.OrderId == o.Id && (l.ProviderReference == term || l.EntityId == term)));

        if (!int.TryParse(term, out var id) || id <= 0)
            throw new ArgumentException("Mã đơn, mã thanh toán hoặc mã hoàn tiền phải là số nguyên dương.");
        return type switch
        {
            "payment" => query.Where(o => db.Payments.Any(p => p.OrderId == o.Id && p.Id == id)),
            "refund" => query.Where(o => db.Refunds.Any(r => r.OrderId == o.Id && r.Id == id)),
            _ => query.Where(o => o.Id == id)
        };
    }

    // SQL datetime/datetime2 values are stored as UTC but materialized with Kind=Unspecified.
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local
        ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? UtcOrNull(DateTime? value) => value is null ? null : Utc(value.Value);

    private static string PaymentLabel(bool success, bool pending, bool verifying, bool failed) =>
        success ? failed ? "Thành công · có lần lỗi" : "Thành công" : verifying ? "Đang xác minh" : pending ? "Đang chờ" : failed ? "Thất bại" : "Chưa thanh toán";

    private static string RefundLabel(bool pending, bool success, bool failed) =>
        string.Join(" · ", new[] { pending ? "Đang chờ" : null, success ? "Thành công" : null, failed ? "Thất bại" : null }.Where(x => x is not null)) is { Length: > 0 } label ? label : "Chưa có";

    private static string? MaskPerson(string? username, string? email)
    {
        if (!string.IsNullOrWhiteSpace(username)) return username.Contains('@') ? MaskEmail(username) : username;
        if (string.IsNullOrWhiteSpace(email)) return null;
        return MaskEmail(email);
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at < 0 ? "•••" : at <= 1 ? "•••" + email[at..] : email[..1] + "•••" + email[at..];
    }

    private static string? MaskReference(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= 4 ? "••••" : $"••••{value[^4..]}";
}
