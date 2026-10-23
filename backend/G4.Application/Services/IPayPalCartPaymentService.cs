using G4.Contracts.Checkout;

namespace G4.Application.Services;

public interface IPayPalCartPaymentService
{
    Task<PayPalCartView> StartAsync(
        int buyerId,
        PayPalCartStartInput input,
        CancellationToken ct = default
    );
    Task<PayPalCartView> CaptureAsync(
        int buyerId,
        long id,
        string providerOrderId,
        CancellationToken ct = default
    );
    Task<PayPalCartView> ReconcileAsync(int buyerId, long id, CancellationToken ct = default);
    Task<PayPalCartView> GetAsync(int buyerId, long id, CancellationToken ct = default);
    Task<PayPalCartView> CancelAsync(int buyerId, long id, CancellationToken ct = default);
    Task<int> MaintainAsync(CancellationToken ct = default);
}
