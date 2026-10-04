using G4.Domain.Entities;

namespace G4.Application.Services;

public interface IShipmentService
{
    Task<ShippingInfo> CreateOutboundAsync(int orderId, CancellationToken ct = default);
    Task<ShippingInfo> CreateReturnAsync(int orderId, CancellationToken ct = default);
    Task<ShippingInfo> ClaimAsync(int shippingInfoId, int shipperId, CancellationToken ct = default);
    Task<bool> RecordEventAsync(int shippingInfoId, string eventId, string nextStatus,
        string? location = null, string? note = null, CancellationToken ct = default, int? shipperId = null);
}
