namespace G4.Contracts.Checkout;

public sealed record CartLine(int ProductId, int Quantity);
public sealed record CheckoutRequest(int AddressId, IReadOnlyList<CartLine> Items, string? CouponCode, string CheckoutKey,
    decimal? ExpectedTotal = null, string? PricingFingerprint = null);
public sealed record CardPaymentRequest(string Number, string Expiry, string Key);
public sealed record IdempotencyKeyRequest(string Key);
public sealed record ShippingEventRequest(string Status, string? EventId, string? Location, string? Note);
public sealed record ReasonRequest(string Reason);
public sealed record CancellationDecisionRequest(bool Approve, string? Reason);
public sealed record CarrierFailureRequest(int OrderId, string Direction, int Count);
public sealed record AddressUpdateRequest(string FullName, string Street, string City, string State, string Country);
