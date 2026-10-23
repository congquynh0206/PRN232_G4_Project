namespace G4.Contracts.Checkout;

public sealed record AddressChangeInput(
    AddressUpdateRequest Address,
    string Reason,
    string Key,
    string? QuoteToken = null
);

public sealed record AddressChangeDecision(bool Approve, string? Reason);

public sealed record AddressChangeShipperDecision(int RequestId, bool Approve, string? Reason);

public sealed record AddressChangeQuoteView(
    string Token,
    string OldAddress,
    string NewAddress,
    decimal OldShippingCost,
    decimal NewShippingCost,
    decimal SellerCap,
    decimal BuyerRefund,
    int? ShipperId,
    bool WithinCap
);

public sealed record AddressChangeCapInput(decimal Amount);

public sealed record AddressChangeView(
    int Id,
    int OrderId,
    string Reason,
    string OldAddress,
    string NewAddress,
    decimal OldShippingCost,
    decimal NewShippingCost,
    decimal SellerCap,
    decimal BuyerRefund,
    string Status,
    string? DecisionReason,
    DateTime CreatedAt,
    DateTime? DecidedAt,
    int WorkflowVersion = 0,
    DateTime? ExpiresAt = null,
    DateTime? BuyerConfirmedAt = null,
    DateTime? SellerConfirmedAt = null,
    int? ExpectedShipperId = null,
    DateTime? ShipperDecidedAt = null,
    DateTime? AppliedAt = null,
    int? BuyerUserId = null,
    int? SellerUserId = null,
    int? ShipperUserId = null
);
