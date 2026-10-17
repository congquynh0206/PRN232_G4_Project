using G4.Contracts.Checkout;

namespace G4.Application.Services;

public interface IProductFeedbackService
{
    Task<ProductFeedbackProducts> ProductsAsync(int sellerId, ProductFeedbackFilter filter, CancellationToken ct = default);
    Task<ProductFeedbackReviews> ReviewsAsync(int sellerId, int productId, ProductFeedbackFilter filter, CancellationToken ct = default);
    Task<ProductReplyView> ReplyAsync(int sellerId, int reviewId, ProductReplyInput input, CancellationToken ct = default);
    Task<BuyerProductReviews> BuyerOrderAsync(int buyerId, int orderId, CancellationToken ct = default);
    Task<ProductReviewView> SubmitAsync(int buyerId, int orderId, int productId, ProductReviewInput input, CancellationToken ct = default);
}
