using backend.Models;

namespace backend.Checkout;

public sealed class RefundGateway(IPayPalGateway paypal) : IRefundGateway
{
    public Task<string> RefundAsync(Payment payment, decimal amount, string key, CancellationToken ct) =>
        payment.Method == "Card"
            ? Task.FromResult("CARD-REFUND-" + payment.Id)
            : payment.Method == "PayPal" && payment.ProviderTransactionId is not null
                ? paypal.RefundAsync(payment.ProviderTransactionId, amount, "USD", key, ct)
                : throw new InvalidOperationException("Payment cannot be refunded");
}
