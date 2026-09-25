namespace backend.Checkout;

public sealed record PayPalCreated(string Id, string ApprovalUrl);
public sealed record PayPalCaptured(string Status, string? CaptureId, decimal Amount, string Currency);

public interface IPayPalGateway
{
    Task<PayPalCreated> CreateAsync(decimal amount, string currency, string requestId, string returnUrl, string cancelUrl, CancellationToken ct);
    Task<PayPalCaptured> CaptureAsync(string orderId, string requestId, CancellationToken ct);
    Task<PayPalCaptured> GetAsync(string orderId, CancellationToken ct);
    Task<string> RefundAsync(string captureId, decimal amount, string currency, string requestId, CancellationToken ct);
}
