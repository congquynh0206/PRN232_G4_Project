using backend.Models;

namespace backend.Checkout;

public interface IRefundGateway
{
    Task<string> RefundAsync(Payment payment, decimal amount, string key, CancellationToken ct);
}
