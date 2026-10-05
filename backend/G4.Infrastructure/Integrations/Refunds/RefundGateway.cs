using G4.Domain.Entities;
using G4.Infrastructure.Diagnostics;
namespace G4.Infrastructure.Integrations.Refunds;
public sealed class RefundGateway(IPayPalGateway paypal, IIntegrationLogWriter? logs = null) : IRefundGateway
{
    public async Task<string> RefundAsync(Payment payment, decimal amount, string key, CancellationToken ct)
    {
        using var context = IntegrationContext.ForOrder(payment.OrderId);
        if (payment.Method == "Card")
        {
            await using var call = new IntegrationCallRecorder(logs).Start("Card", "Refund", "Simulated", payment.Id.ToString());
            var reference = "CARD-REFUND-" + payment.Id;
            await call.CompleteAsync("Succeeded", providerReference: reference, ct: ct);
            return reference;
        }
        if (payment.Method == "PayPal" && payment.ProviderTransactionId != null)
            return await paypal.RefundAsync(payment.ProviderTransactionId, amount, "USD", key, ct);
        throw new InvalidOperationException("Payment cannot be refunded");
    }
}
