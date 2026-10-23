namespace G4.Contracts.Checkout;

public sealed record ProductReviewInput(int Rating, string? Comment, string Key);

public sealed record ProductReplyInput(string Comment, string Key);

public sealed class ProductFeedbackFilter
{
    public string? Search { get; set; }
    public int? Rating { get; set; }
    public string Reply { get; set; } = "all";
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed record ProductFeedbackSummary(
    int ProductCount,
    int ReviewCount,
    decimal? AverageRating,
    int UnansweredCount
);

public sealed record ProductFeedbackProduct(
    int ProductId,
    string Title,
    string? ImageUrl,
    int ReviewCount,
    decimal AverageRating,
    int UnansweredCount,
    DateTime? LatestAt
);

public sealed record ProductFeedbackProducts(
    int Page,
    int PageSize,
    int TotalCount,
    ProductFeedbackSummary Summary,
    IReadOnlyList<ProductFeedbackProduct> Items
);

public sealed record ProductReplyView(int Id, string Comment, DateTime CreatedAt);

public sealed record ProductReviewView(
    int Id,
    int? OrderId,
    int Rating,
    string Comment,
    string BuyerName,
    DateTime? CreatedAt,
    bool VerifiedPurchase,
    ProductReplyView? Reply
);

public sealed record ProductFeedbackReviews(
    int ProductId,
    string Title,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<ProductReviewView> Items
);

public sealed record BuyerProductReviewItem(
    int ProductId,
    string Title,
    string? ImageUrl,
    bool CanReview,
    ProductReviewView? Review
);

public sealed record BuyerProductReviews(int OrderId, IReadOnlyList<BuyerProductReviewItem> Items);
