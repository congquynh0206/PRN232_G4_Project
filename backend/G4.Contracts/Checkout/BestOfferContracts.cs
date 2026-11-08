namespace G4.Contracts.Checkout;

public sealed record BestOfferCreateInput(
    int ProductId,
    decimal Amount,
    int AddressId,
    string PaymentMethod,
    string CardProfile,
    string CardExpiry,
    string Key
);

public sealed record BestOfferActionInput(
    decimal? Amount = null,
    string? CardProfile = null,
    string? CardExpiry = null,
    string? Key = null
);

public sealed record BestOfferSettingsInput(
    int ProductId,
    bool AllowOffers,
    decimal? AutoAcceptPrice,
    decimal? AutoDeclineBelow
);

public sealed record BestOfferTurnView(
    long Id,
    int ActorId,
    string ActorRole,
    decimal Amount,
    string Action,
    string? Result,
    DateTime CreatedAt
);

public sealed record BestOfferView(
    long Id,
    int ProductId,
    string ProductTitle,
    int BuyerId,
    int SellerId,
    decimal ListPrice,
    decimal CurrentAmount,
    string Status,
    int BuyerOfferCount,
    int BuyerOfferLimit,
    DateTime ExpiresAt,
    int? OrderId,
    string ViewerRole,
    IReadOnlyList<BestOfferTurnView> Turns
);

public sealed record OfferableProductView(
    int ProductId,
    string Title,
    string? ImageUrl,
    decimal ListPrice,
    decimal WeightKg,
    int Available,
    int SellerId
);
