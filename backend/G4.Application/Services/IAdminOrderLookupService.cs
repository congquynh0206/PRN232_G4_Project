using G4.Contracts.Checkout;

namespace G4.Application.Services;

public interface IAdminOrderLookupService
{
    Task<AdminOrderLookupPage> SearchAsync(
        AdminOrderLookupQuery query,
        CancellationToken ct = default
    );
    Task<AdminOrderLookupDetail> DetailAsync(int orderId, CancellationToken ct = default);
}
