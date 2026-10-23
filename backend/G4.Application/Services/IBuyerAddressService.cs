using G4.Contracts.Checkout;

namespace G4.Application.Services;

public interface IBuyerAddressService
{
    Task<IReadOnlyList<BuyerAddressView>> ListAsync(int buyerId, CancellationToken ct = default);
    Task<BuyerAddressBook> CreateAsync(
        int buyerId,
        BuyerAddressInput input,
        CancellationToken ct = default
    );
    Task<BuyerAddressBook> UpdateAsync(
        int buyerId,
        int id,
        BuyerAddressInput input,
        CancellationToken ct = default
    );
    Task<BuyerAddressBook> SetDefaultAsync(int buyerId, int id, CancellationToken ct = default);
    Task<BuyerAddressBook> DeleteAsync(int buyerId, int id, CancellationToken ct = default);
}
