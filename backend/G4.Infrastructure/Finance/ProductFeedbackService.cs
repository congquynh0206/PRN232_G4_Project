using System.Data;
using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Finance;

public sealed class ProductFeedbackService(ApplicationDbContext db, TimeProvider clock)
    : IProductFeedbackService
{
    private IQueryable<Review> Filter(int sellerId, ProductFeedbackFilter f)
    {
        if (
            f.Rating.HasValue && f.Rating is < 1 or > 5
            || f.Reply is not ("all" or "answered" or "unanswered")
            || f.From > f.To
            || (f.Search?.Length ?? 0) > 100
        )
            throw new ArgumentException(
                "Bộ lọc đánh giá không hợp lệ; kiểm tra số sao và khoảng thời gian."
            );
        var q = db
            .Reviews.AsNoTracking()
            .Where(r =>
                r.Product != null
                && r.Product.SellerId == sellerId
                && r.Rating >= 1
                && r.Rating <= 5
            );
        var search = f.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var productId = int.TryParse(search.TrimStart('#'), out var id) ? id : -1;
            q = q.Where(r =>
                r.ProductId == productId
                || r.Product!.Title != null && r.Product.Title.Contains(search)
            );
        }
        if (f.Rating.HasValue)
            q = q.Where(r => r.Rating == f.Rating);
        if (f.From.HasValue)
        {
            var from = f.From.Value.UtcDateTime;
            q = q.Where(r => r.CreatedAt >= from);
        }
        if (f.To.HasValue)
        {
            var to = f.To.Value.UtcDateTime;
            q = q.Where(r => r.CreatedAt < to);
        }
        if (f.Reply == "answered")
            q = q.Where(r => db.ProductReviewReplies.Any(x => x.ReviewId == r.Id));
        if (f.Reply == "unanswered")
            q = q.Where(r => !db.ProductReviewReplies.Any(x => x.ReviewId == r.Id));
        return q;
    }

    private static (int Page, int Size) Paging(ProductFeedbackFilter f, int total)
    {
        var size = Math.Clamp(f.PageSize, 1, 50);
        return (Math.Clamp(f.Page, 1, Math.Max(1, (total + size - 1) / size)), size);
    }

    public async Task<ProductFeedbackProducts> ProductsAsync(
        int sellerId,
        ProductFeedbackFilter filter,
        CancellationToken ct = default
    )
    {
        var q = Filter(sellerId, filter);
        var count = await q.CountAsync(ct);
        var unanswered = await q.CountAsync(
            r => !db.ProductReviewReplies.Any(x => x.ReviewId == r.Id),
            ct
        );
        var average =
            count == 0
                ? (decimal?)null
                : (decimal)await q.AverageAsync(r => (double)r.Rating!.Value, ct);
        var productCount = await q.Select(r => r.ProductId).Distinct().CountAsync(ct);
        var (page, size) = Paging(filter, productCount);
        var groups = q.GroupBy(r => r.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Count = g.Count(),
                Average = g.Average(r => (double)r.Rating!.Value),
                Latest = g.Max(r => r.CreatedAt),
            });
        var products = await (
            from g in groups
            join p in db.Products on g.ProductId equals (int?)p.Id
            orderby g.Latest descending, p.Id descending
            select new
            {
                p.Id,
                p.Title,
                p.Images,
                g.Count,
                g.Average,
                g.Latest,
            }
        )
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);
        var ids = products.Select(x => x.Id).ToArray();
        var missing = await q.Where(r =>
                ids.Contains(r.ProductId!.Value)
                && !db.ProductReviewReplies.Any(x => x.ReviewId == r.Id)
            )
            .GroupBy(r => r.ProductId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return new(
            page,
            size,
            productCount,
            new(
                productCount,
                count,
                average == null ? null : decimal.Round(average.Value, 2),
                unanswered
            ),
            products
                .Select(x => new ProductFeedbackProduct(
                    x.Id,
                    x.Title ?? "Sản phẩm",
                    x.Images,
                    x.Count,
                    decimal.Round((decimal)x.Average, 2),
                    missing.GetValueOrDefault(x.Id),
                    Utc(x.Latest)
                ))
                .ToArray()
        );
    }

    public async Task<ProductFeedbackReviews> ReviewsAsync(
        int sellerId,
        int productId,
        ProductFeedbackFilter filter,
        CancellationToken ct = default
    )
    {
        var product =
            await db
                .Products.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == productId && p.SellerId == sellerId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy sản phẩm của bạn.");
        var q = Filter(sellerId, filter).Where(r => r.ProductId == productId);
        var total = await q.CountAsync(ct);
        var (page, size) = Paging(filter, total);
        var rows = await q.OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);
        return new(
            productId,
            product.Title ?? "Sản phẩm",
            page,
            size,
            total,
            await ViewsAsync(rows, ct)
        );
    }

    public async Task<BuyerProductReviews> BuyerOrderAsync(
        int buyerId,
        int orderId,
        CancellationToken ct = default
    )
    {
        var order = await BuyerOrder(buyerId, orderId, ct);
        var canReview = await Eligible(order, ct);
        var items = await db
            .OrderItems.AsNoTracking()
            .Where(i => i.OrderId == orderId && i.ProductId != null)
            .Select(i => new
            {
                i.ProductId,
                i.ProductTitleSnapshot,
                Image = i.Product == null ? null : i.Product.Images,
                SellerId = i.Product == null ? null : i.Product.SellerId,
            })
            .ToListAsync(ct);
        var reviews = await db
            .Reviews.AsNoTracking()
            .Where(r => r.OrderId == orderId && r.ReviewerId == buyerId)
            .ToListAsync(ct);
        var views = (await ViewsAsync(reviews, ct)).ToDictionary(r => r.Id);
        return new(
            orderId,
            items
                .GroupBy(i => i.ProductId!.Value)
                .Select(g =>
                {
                    var item = g.First();
                    var review = reviews.SingleOrDefault(r => r.ProductId == g.Key);
                    return new BuyerProductReviewItem(
                        g.Key,
                        item.ProductTitleSnapshot ?? "Sản phẩm",
                        item.Image,
                        canReview && review == null && item.SellerId == order.SellerId,
                        review == null ? null : views[review.Id]
                    );
                })
                .ToArray()
        );
    }

    public async Task<ProductReviewView> SubmitAsync(
        int buyerId,
        int orderId,
        int productId,
        ProductReviewInput input,
        CancellationToken ct = default
    )
    {
        var key = Key(input.Key);
        var comment = (input.Comment ?? "").Trim();
        if (input.Rating is < 1 or > 5 || comment.Length > 2000)
            throw new ArgumentException("Chọn 1–5 sao; nhận xét tối đa 2.000 ký tự.");
        await using var tx =
            db.Database.IsRelational() && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                : null;
        await LockAsync($"product-review:buyer:{buyerId}", ct);
        var order = await BuyerOrder(buyerId, orderId, ct);
        if (
            !await db.OrderItems.AnyAsync(
                i =>
                    i.OrderId == orderId
                    && i.ProductId == productId
                    && i.Product != null
                    && i.Product.SellerId == order.SellerId,
                ct
            )
        )
            throw new InvalidOperationException("Sản phẩm không thuộc đơn đã mua.");
        var old = await db
            .Reviews.AsNoTracking()
            .SingleOrDefaultAsync(
                r =>
                    r.OrderId == orderId && r.ProductId == productId
                    || r.ReviewerId == buyerId && r.RequestKey == key,
                ct
            );
        if (old != null)
        {
            if (
                old.OrderId != orderId
                || old.ProductId != productId
                || old.ReviewerId != buyerId
                || old.RequestKey != key
                || old.Rating != input.Rating
                || old.Comment != comment
            )
                throw new InvalidOperationException(
                    "Sản phẩm trong đơn đã được đánh giá hoặc mã yêu cầu đã dùng."
                );
            if (tx != null)
                await tx.CommitAsync(ct);
            return (await ViewsAsync([old], ct)).Single();
        }
        if (!await Eligible(order, ct))
            throw new InvalidOperationException(
                "Chỉ đánh giá sản phẩm của đơn đã thanh toán và giao thành công hoặc hoàn tất."
            );
        var review = new Review
        {
            ProductId = productId,
            ReviewerId = buyerId,
            OrderId = orderId,
            Rating = input.Rating,
            Comment = comment,
            RequestKey = key,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return (await ViewsAsync([review], ct)).Single();
    }

    public async Task<ProductReplyView> ReplyAsync(
        int sellerId,
        int reviewId,
        ProductReplyInput input,
        CancellationToken ct = default
    )
    {
        var key = Key(input.Key);
        var comment = (input.Comment ?? "").Trim();
        if (comment.Length is < 1 or > 2000)
            throw new ArgumentException("Phản hồi cần từ 1 đến 2.000 ký tự.");
        await using var tx =
            db.Database.IsRelational() && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                : null;
        await LockAsync($"product-review:seller:{sellerId}", ct);
        var review =
            await db
                .Reviews.AsNoTracking()
                .SingleOrDefaultAsync(
                    r =>
                        r.Id == reviewId
                        && r.Product != null
                        && r.Product.SellerId == sellerId
                        && r.Rating >= 1
                        && r.Rating <= 5,
                    ct
                )
            ?? throw new KeyNotFoundException("Không tìm thấy đánh giá thuộc sản phẩm của bạn.");
        var old = await db
            .ProductReviewReplies.AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.ReviewId == reviewId || r.SellerId == sellerId && r.RequestKey == key,
                ct
            );
        if (old != null)
        {
            if (
                old.ReviewId != reviewId
                || old.SellerId != sellerId
                || old.RequestKey != key
                || old.Comment != comment
            )
                throw new InvalidOperationException(
                    "Đánh giá đã được trả lời hoặc mã yêu cầu đã dùng."
                );
            if (tx != null)
                await tx.CommitAsync(ct);
            return ReplyView(old);
        }
        var reply = new ProductReviewReply
        {
            ReviewId = reviewId,
            SellerId = sellerId,
            Comment = comment,
            RequestKey = key,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.ProductReviewReplies.Add(reply);
        db.CommerceChanges.Add(
            new CommerceChange
            {
                Topic = "product-feedback",
                SellerId = sellerId,
                BuyerId = review.ReviewerId,
                OrderId = review.OrderId,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            }
        );
        await db.SaveChangesAsync(ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return ReplyView(reply);
    }

    private async Task<OrderTable> BuyerOrder(int buyerId, int orderId, CancellationToken ct)
    {
        var order =
            await db.OrderTables.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy đơn hàng.");
        if (order.BuyerId != buyerId || order.SellerId == buyerId)
            throw new UnauthorizedAccessException("Bạn không được xem hoặc đánh giá đơn này.");
        return order;
    }

    private async Task<bool> Eligible(OrderTable order, CancellationToken ct) =>
        order.SellerId != null
        && order.Status is "Delivered" or "Closed"
        && await db.Payments.AnyAsync(p => p.OrderId == order.Id && p.Status == "Succeeded", ct);

    private static string Key(string? value)
    {
        var key = value?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > 100)
            throw new ArgumentException("Mã yêu cầu không hợp lệ.");
        return key;
    }

    private async Task LockAsync(string resource, CancellationToken ct)
    {
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"DECLARE @r int;
            EXEC @r = sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
            IF @r < 0 THROW 51000, 'Cannot acquire product feedback lock', 1;",
                ct
            );
    }

    private static DateTime? Utc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

    private static ProductReplyView ReplyView(ProductReviewReply reply) =>
        new(reply.Id, reply.Comment, Utc(reply.CreatedAt)!.Value);

    private async Task<IReadOnlyList<ProductReviewView>> ViewsAsync(
        IReadOnlyList<Review> rows,
        CancellationToken ct
    )
    {
        var ids = rows.Select(r => r.Id).ToArray();
        var buyerIds = rows.Select(r => r.ReviewerId).ToArray();
        var replies = await db
            .ProductReviewReplies.AsNoTracking()
            .Where(r => ids.Contains(r.ReviewId))
            .ToDictionaryAsync(r => r.ReviewId, ct);
        var buyers = await db
            .Users.AsNoTracking()
            .Where(u => buyerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        return rows.Select(r => new ProductReviewView(
                r.Id,
                r.OrderId,
                r.Rating ?? 0,
                r.Comment ?? "",
                buyers.GetValueOrDefault(r.ReviewerId ?? 0) ?? "Người mua",
                Utc(r.CreatedAt),
                r.OrderId != null,
                replies.TryGetValue(r.Id, out var reply) ? ReplyView(reply) : null
            ))
            .ToArray();
    }
}
