using G4.Contracts.Checkout;

namespace G4.Application.Services;

public interface IBestOfferService
{
    Task<IReadOnlyList<OfferableProductView>> GetOfferableProductsAsync(CancellationToken ct);
    Task<IReadOnlyList<BestOfferView>> GetBuyerOffersAsync(int buyerId, CancellationToken ct);
    Task<IReadOnlyList<BestOfferView>> GetSellerOffersAsync(int sellerId, CancellationToken ct);
    Task<BestOfferView> CreateAsync(int buyerId, BestOfferCreateInput input, CancellationToken ct);
    Task<BestOfferView> SellerAcceptAsync(int sellerId, long id, CancellationToken ct);
    Task<BestOfferView> SellerDeclineAsync(int sellerId, long id, CancellationToken ct);
    Task<BestOfferView> SellerCounterAsync(
        int sellerId,
        long id,
        BestOfferActionInput input,
        CancellationToken ct
    );
    Task<BestOfferView> BuyerAcceptAsync(int buyerId, long id, CancellationToken ct);
    Task<BestOfferView> UpdatePaymentProfileAsync(
        int buyerId,
        long id,
        BestOfferActionInput input,
        CancellationToken ct
    );
    Task<BestOfferView> BuyerDeclineAsync(int buyerId, long id, CancellationToken ct);
    Task<BestOfferView> BuyerCounterAsync(
        int buyerId,
        long id,
        BestOfferActionInput input,
        CancellationToken ct
    );
    Task<int> ExpireAsync(CancellationToken ct);
    Task UpdateProductSettingsAsync(int sellerId, BestOfferSettingsInput input, CancellationToken ct);
}
