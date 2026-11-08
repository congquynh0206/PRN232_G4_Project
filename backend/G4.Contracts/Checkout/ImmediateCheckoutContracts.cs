namespace G4.Contracts.Checkout;

public sealed record ImmediateCheckoutInput(
    int ProductId,
    int Quantity,
    int AddressId,
    string? CouponCode
);

public sealed record ProductPaymentSettings(
    bool ImmediatePaymentRequired,
    bool IsListed,
    decimal ExpectedPrice,
    decimal Price
);
