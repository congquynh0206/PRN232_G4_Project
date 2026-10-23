using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

internal static class ProductFeedbackChecks
{
    internal static readonly DateTime Now = new(2026, 10, 17, 5, 0, 0, DateTimeKind.Utc);

    internal sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    internal sealed record Fixture(
        int Seller,
        int OtherSeller,
        int Buyer,
        int OtherBuyer,
        int Order,
        int Product,
        int OtherProduct,
        int LegacyReview
    );

    internal static void Check(bool ok, string message)
    {
        if (!ok)
            throw new Exception("Product feedback: " + message);
    }

    internal static async Task Reject<T>(Func<Task> action)
        where T : Exception
    {
        try
        {
            await action();
        }
        catch (T)
        {
            return;
        }
        throw new Exception("Expected " + typeof(T).Name);
    }

    internal static async Task<Fixture> Seed(ApplicationDbContext db)
    {
        var salt = Guid.NewGuid().ToString("N");
        var seller = new User
        {
            Role = "seller",
            Username = "Shop <A>",
            Email = $"feedback-seller-{salt}@example.test",
        };
        var otherSeller = new User
        {
            Role = "seller",
            Username = "Other shop",
            Email = $"feedback-other-seller-{salt}@example.test",
        };
        var buyer = new User
        {
            Role = "buyer",
            Username = "Buyer <img>",
            Email = $"feedback-buyer-{salt}@example.test",
        };
        var otherBuyer = new User
        {
            Role = "buyer",
            Username = "Other buyer",
            Email = $"feedback-other-buyer-{salt}@example.test",
        };
        db.Users.AddRange(seller, otherSeller, buyer, otherBuyer);
        await db.SaveChangesAsync();
        var product = new Product
        {
            SellerId = seller.Id,
            Title = "Headphones <script>",
            Price = 25m,
            WeightKg = .3m,
            IsAuction = false,
        };
        var other = new Product
        {
            SellerId = otherSeller.Id,
            Title = "Other product",
            Price = 30m,
            WeightKg = .2m,
            IsAuction = false,
        };
        db.Products.AddRange(product, other);
        await db.SaveChangesAsync();
        var order = new OrderTable
        {
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            Status = "Delivered",
            OrderDate = Now.AddDays(-2),
            UpdatedAt = Now,
            TotalPrice = 25m,
        };
        db.OrderTables.Add(order);
        await db.SaveChangesAsync();
        db.OrderItems.Add(
            new OrderItem
            {
                OrderId = order.Id,
                ProductId = product.Id,
                Quantity = 1,
                UnitPrice = 25m,
                ProductTitleSnapshot = product.Title,
            }
        );
        db.Payments.Add(
            new Payment
            {
                OrderId = order.Id,
                UserId = buyer.Id,
                Method = "Card",
                Status = "Succeeded",
                Amount = 25m,
                CreatedAt = Now.AddDays(-2),
                PaidAt = Now.AddDays(-2),
                IdempotencyKey = "paid-" + salt,
            }
        );
        var legacy = new Review
        {
            ProductId = product.Id,
            ReviewerId = otherBuyer.Id,
            Rating = 2,
            Comment = "Old review",
            CreatedAt = Now.AddDays(-1),
        };
        db.Reviews.AddRange(
            legacy,
            new Review
            {
                ProductId = other.Id,
                ReviewerId = otherBuyer.Id,
                Rating = 1,
                Comment = "Private other shop",
                CreatedAt = Now,
            }
        );
        await db.SaveChangesAsync();
        return new(
            seller.Id,
            otherSeller.Id,
            buyer.Id,
            otherBuyer.Id,
            order.Id,
            product.Id,
            other.Id,
            legacy.Id
        );
    }

    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase("product-feedback-" + Guid.NewGuid())
                .Options
        );
        var f = await Seed(db);
        var service = new ProductFeedbackService(db, new Clock());
        await Reject<UnauthorizedAccessException>(() =>
            service.SubmitAsync(f.OtherBuyer, f.Order, f.Product, new(5, "wrong buyer", "a"))
        );
        await Reject<InvalidOperationException>(() =>
            service.SubmitAsync(f.Buyer, f.Order, f.OtherProduct, new(5, "not purchased", "a"))
        );
        await Reject<ArgumentException>(() =>
            service.SubmitAsync(f.Buyer, f.Order, f.Product, new(0, "bad rating", "a"))
        );
        var order = await db.OrderTables.SingleAsync(x => x.Id == f.Order);
        order.Status = "Paid";
        await db.SaveChangesAsync();
        await Reject<InvalidOperationException>(() =>
            service.SubmitAsync(f.Buyer, f.Order, f.Product, new(5, "too soon", "a"))
        );
        order.Status = "Delivered";
        await db.SaveChangesAsync();
        var review = await service.SubmitAsync(
            f.Buyer,
            f.Order,
            f.Product,
            new(5, " Good <script> ", "review-key")
        );
        var retry = await service.SubmitAsync(
            f.Buyer,
            f.Order,
            f.Product,
            new(5, "Good <script>", "review-key")
        );
        Check(
            review.Id == retry.Id && review.VerifiedPurchase && review.Comment == "Good <script>",
            "trimmed review saved once on retry"
        );
        await Reject<InvalidOperationException>(() =>
            service.SubmitAsync(f.Buyer, f.Order, f.Product, new(4, "changed", "review-key"))
        );
        await Reject<InvalidOperationException>(() =>
            service.SubmitAsync(f.Buyer, f.Order, f.Product, new(5, "Good <script>", "another-key"))
        );
        var all = await service.ProductsAsync(f.Seller, new() { PageSize = 1 });
        Check(
            all.Summary.ReviewCount == 2
                && all.Summary.AverageRating == 3.5m
                && all.Summary.ProductCount == 1
                && all.Summary.UnansweredCount == 2,
            "summary is product-specific, excludes other seller"
        );
        await Reject<KeyNotFoundException>(() =>
            service.ReviewsAsync(f.OtherSeller, f.Product, new())
        );
        await Reject<KeyNotFoundException>(() =>
            service.ReplyAsync(f.OtherSeller, review.Id, new("wrong seller", "reply"))
        );
        await Reject<ArgumentException>(() =>
            service.ReplyAsync(f.Seller, review.Id, new(" ", "reply"))
        );
        var reply = await service.ReplyAsync(
            f.Seller,
            review.Id,
            new(" Thank you <img> ", "reply-key")
        );
        Check(
            (await service.ReplyAsync(f.Seller, review.Id, new("Thank you <img>", "reply-key"))).Id
                == reply.Id,
            "reply retry returns same reply"
        );
        await Reject<InvalidOperationException>(() =>
            service.ReplyAsync(f.Seller, review.Id, new("changed", "reply-key"))
        );
        var answered = await service.ProductsAsync(
            f.Seller,
            new()
            {
                Rating = 5,
                Reply = "answered",
                Search = "Headphones",
                From = new(Now),
                To = new(Now.AddDays(1)),
            }
        );
        Check(
            answered.Summary.ReviewCount == 1
                && answered.Summary.UnansweredCount == 0
                && answered.Summary.AverageRating == 5m,
            "combined rating/reply/name/date filters apply to all totals"
        );
        Check(
            (await service.ProductsAsync(f.Seller, new() { Reply = "unanswered" }))
                .Summary
                .ReviewCount == 1,
            "unanswered filter updates after reply"
        );
        Check(
            (await service.ProductsAsync(f.Seller, new() { Search = "#" + f.Product })).TotalCount
                == 1,
            "product ID search"
        );
        var page = await service.ReviewsAsync(
            f.Seller,
            f.Product,
            new() { PageSize = 1, Page = 999 }
        );
        Check(
            page.Page == 2
                && page.TotalCount == 2
                && page.Items.Count == 1
                && !page.Items[0].VerifiedPurchase,
            "paging clamps and legacy review has no purchase claim"
        );
        var buyer = await service.BuyerOrderAsync(f.Buyer, f.Order);
        Check(
            !buyer.Items.Single().CanReview
                && buyer.Items.Single().Review?.Reply?.Comment == "Thank you <img>",
            "buyer sees shop reply to purchased product"
        );
        await Reject<UnauthorizedAccessException>(() =>
            service.BuyerOrderAsync(f.OtherBuyer, f.Order)
        );
        await Reject<ArgumentException>(() =>
            service.ProductsAsync(f.Seller, new() { From = new(Now), To = new(Now.AddDays(-1)) })
        );
        Check(
            !await db.SellerOrderFeedbacks.AnyAsync()
                && !await db.SellerLevelEvaluations.AnyAsync()
                && !await db.SellerSettlements.AnyAsync(),
            "product reviews do not affect seller levels or finance"
        );
        var trackedReply = await db.ProductReviewReplies.SingleAsync();
        trackedReply.Comment = "modified";
        await Reject<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(trackedReply).State = EntityState.Unchanged;
        var trackedReview = await db.Reviews.SingleAsync(x => x.Id == review.Id);
        trackedReview.Comment = "modified";
        await Reject<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(trackedReview).State = EntityState.Unchanged;
        Console.WriteLine(
            "Product feedback checks passed: ownership, purchase eligibility, idempotency, filters, summaries, paging, immutable replies and buyer visibility."
        );
    }
}
