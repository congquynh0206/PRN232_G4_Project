using G4.Domain.Entities;

namespace G4.Application.Services;

public interface IPayPalPaymentService
{
    Task<PayPalCreated> StartAsync(int orderId, string key, CancellationToken ct = default);
    Task<Payment> CaptureAsync(int orderId, string providerOrderId, CancellationToken ct = default);
    Task<Payment> ReconcileAsync(int orderId, CancellationToken ct = default);
}
