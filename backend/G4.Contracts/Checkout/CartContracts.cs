namespace G4.Contracts.Checkout;
public sealed record CartItem(
    int ProductId,
    string? Title,
    decimal? Price,
    decimal? WeightKg,
    string? ImageUrl,
    int Quantity,
    int Available,
    int? SellerId,
    string SellerName
);
public sealed record BuyerCartResponse(Guid Version, IReadOnlyList<CartItem> Items);
public sealed record SaveCartRequest(Guid Version, IReadOnlyList<CartLine> Items);
public sealed record RandomCartRequest(Guid Version, int Count = 3);
public sealed record SellerCheckoutRequest(
    int SellerId,
    string? CouponCode = null,
    decimal? ExpectedTotal = null,
    string? PricingFingerprint = null
);
public sealed record CartCheckoutRequest(
    int AddressId,
    Guid CartVersion,
    IReadOnlyList<SellerCheckoutRequest> Sellers,
    string CheckoutKey
);
public sealed record CartOrderResponse(
    int Id,
    int? SellerId,
    string? Status,
    decimal? TotalPrice,
    DateTime? PaymentExpiresAt
);
