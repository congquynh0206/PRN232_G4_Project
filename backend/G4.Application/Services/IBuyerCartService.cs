using G4.Contracts.Checkout;
using G4.Domain.Rules;

namespace G4.Application.Services;
public sealed record SellerCartQuote(int SellerId, string SellerName, PriceQuote Quote);
public sealed record CartQuoteResponse(
    IReadOnlyList<SellerCartQuote> Sellers,
    decimal Subtotal,
    decimal Discount,
    decimal Shipping,
    decimal Total,
    decimal TotalWeightKg
);
public interface IBuyerCartService
{
    Task<BuyerCartResponse> GetAsync(int buyerId, CancellationToken ct = default);
    Task<BuyerCartResponse> SaveAsync(int buyerId, SaveCartRequest request, CancellationToken ct = default);
    Task<BuyerCartResponse> RandomAsync(int buyerId, RandomCartRequest request, CancellationToken ct = default);
    Task<CartQuoteResponse> QuoteAsync(int buyerId, CartCheckoutRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<CartOrderResponse>> CheckoutAsync(int buyerId, CartCheckoutRequest request, CancellationToken ct = default);
}
