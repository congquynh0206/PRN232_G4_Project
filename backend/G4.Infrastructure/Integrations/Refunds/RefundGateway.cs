using G4.Domain.Entities;
using G4.Infrastructure.Diagnostics;

namespace G4.Infrastructure.Integrations.Refunds;

public sealed class RefundGateway(
    IPayPalGateway paypal,
    IIntegrationLogWriter? logs = null,
    IKlarnaGateway? klarna = null,
    IImmediateCardGateway? immediateCards = null
)
    : IRefundGateway
{
    public async Task<string> RefundAsync(
        Payment payment,
        decimal amount,
        string key,
        CancellationToken ct
    )
    {
        using var context = IntegrationContext.ForOrder(payment.OrderId);
        if (payment.Method == "Card")
        {
            if (payment.ProviderTransactionId?.StartsWith("CARD-IP-", StringComparison.Ordinal)
                == true)
            {
                if (immediateCards == null)
                    throw new InvalidOperationException("Immediate card gateway is required");
                var authorizationKey = payment.ProviderTransactionId["CARD-".Length..];
                var result = await immediateCards.RefundAsync(authorizationKey, ct, amount, key);
                if (result.Status is not ("Refunded" or "PartiallyRefunded"))
                    throw new InvalidOperationException("Immediate card refund is not confirmed");
                return "CARD-REFUND-" + authorizationKey;
            }
            await using var call = new IntegrationCallRecorder(logs).Start(
                "Card",
                "Refund",
                "Simulated",
                payment.Id.ToString()
            );
            var reference = "CARD-REFUND-" + payment.Id;
            await call.CompleteAsync("Succeeded", providerReference: reference, ct: ct);
            return reference;
        }

        if (payment.Method == "PayPal" && payment.ProviderTransactionId != null)
            return await paypal.RefundAsync(payment.ProviderTransactionId, amount, "USD", key, ct);
        if (payment.Method == "Klarna" && payment.ProviderOrderId != null && payment.ProviderTransactionId != null)
        {
            if (klarna == null) throw new InvalidOperationException("Klarna gateway unavailable");
            if (amount <= 0 || decimal.Round(amount, 2) != amount)
                throw new ArgumentException("Refund amount must be positive USD cents", nameof(amount));
            return await klarna.RefundAsync(payment.ProviderOrderId, checked((long)(amount * 100m)), key, ct);
        }
        throw new InvalidOperationException("Payment cannot be refunded");
    }
}
