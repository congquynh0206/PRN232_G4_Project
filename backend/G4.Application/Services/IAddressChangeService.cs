using G4.Contracts.Checkout;

namespace G4.Application.Services;

public interface IAddressChangeService
{
    Task<AddressChangeView> RequestAsync(int orderId, int buyerId, AddressChangeInput input, CancellationToken ct = default);
    Task<AddressChangeView> DecideAsync(int id, int sellerId, AddressChangeDecision input, CancellationToken ct = default);
    Task AcknowledgeAsync(int shipmentId, int shipperId, CancellationToken ct = default);
}
