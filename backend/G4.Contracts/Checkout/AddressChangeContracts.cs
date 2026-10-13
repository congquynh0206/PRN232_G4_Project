namespace G4.Contracts.Checkout;

public sealed record AddressChangeInput(AddressUpdateRequest Address, string Reason, string Key);
public sealed record AddressChangeDecision(bool Approve, string? Reason);
public sealed record AddressChangeCapInput(decimal Amount);
public sealed record AddressChangeView(int Id, int OrderId, string Reason, string OldAddress, string NewAddress,
    decimal OldShippingCost, decimal NewShippingCost, decimal SellerCap, decimal BuyerRefund,
    string Status, string? DecisionReason, DateTime CreatedAt, DateTime? DecidedAt);
