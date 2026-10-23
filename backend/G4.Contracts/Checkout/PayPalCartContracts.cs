namespace G4.Contracts.Checkout;

public sealed record PayPalCartStartInput(string CheckoutKey, string Key);

public sealed record PayPalCartCaptureInput(string ProviderOrderId);

public sealed record PayPalCartOrderView(
    int Id,
    string? OrderStatus,
    decimal Amount,
    string Currency,
    string PaymentStatus
);

public sealed record PayPalCartView(
    long Id,
    string CheckoutKey,
    string Status,
    string? ApprovalUrl,
    IReadOnlyList<PayPalCartOrderView> Orders,
    string? Message
);
