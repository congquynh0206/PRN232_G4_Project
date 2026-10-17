using G4.Contracts.Checkout;
namespace G4.Application.Services;
public interface ISellerRegistrationService
{
    Task<SellerRegistrationResult> RegisterAsync(SellerRegistrationInput input, CancellationToken ct = default);
    Task<SellerIdentityProfile> ProfileAsync(int sellerId, CancellationToken ct = default);
    Task<SellerIdentityProfile> ChangeIdentityAsync(int sellerId, SellerIdentityChangeInput input, CancellationToken ct = default);
    Task<IReadOnlyList<SellerRegistrationSample>> SamplesAsync(CancellationToken ct = default);
}
