namespace G4.Application.Integrations;
public sealed record PayPalPurchaseUnit(string ReferenceId, decimal Amount, string Currency);
public sealed record PayPalUnitCapture(string ReferenceId, string Status, string? CaptureId, decimal Amount, string Currency);
public sealed record PayPalBatchCaptured(string ProviderOrderId, string Status, IReadOnlyList<PayPalUnitCapture> Units);
public interface IPayPalBatchGateway
{
    Task<PayPalCreated> CreateBatchAsync(IReadOnlyList<PayPalPurchaseUnit> units, string requestId, string returnUrl, string cancelUrl, CancellationToken ct);
    Task<PayPalBatchCaptured> CaptureBatchAsync(string providerOrderId, string requestId, CancellationToken ct);
    Task<PayPalBatchCaptured> GetBatchAsync(string providerOrderId, CancellationToken ct);
}
