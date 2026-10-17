using G4.Contracts.Checkout;
namespace G4.Application.Services;
public interface ISellerFeedbackService
{
    Task<SellerFeedbackView> SubmitAsync(int orderId, int buyerId, SellerFeedbackInput input, CancellationToken ct = default);
}
