using G4.Domain.Entities;

namespace G4.Application.Integrations;

public interface IRefundGateway
{
    Task<string> RefundAsync(Payment payment, decimal amount, string key, CancellationToken ct);
}
