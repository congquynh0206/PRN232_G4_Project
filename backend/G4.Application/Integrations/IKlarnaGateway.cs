namespace G4.Application.Integrations;

public sealed record KlarnaLine(string Reference, string Name, int Quantity, long UnitPriceMinor, string Type);

public sealed record KlarnaAddress(
    string FullName,
    string Street,
    string City,
    string Region,
    string? PostalCode,
    string CountryCode,
    string? Phone
);

public sealed record KlarnaCheckoutRequest(
    string PurchaseCountry,
    string Locale,
    string Currency,
    long OrderAmountMinor,
    IReadOnlyList<KlarnaLine> Lines,
    string MerchantReference,
    string SuccessUrl,
    string CancelUrl,
    KlarnaAddress ShippingAddress
);

public sealed record KlarnaSession(string KpSessionId, string HppSessionId, string SessionUrl, string RedirectUrl, DateTimeOffset? ExpiresAt = null);
public sealed record KlarnaHppStatus(string Status, string? OrderId);
public sealed record KlarnaOrderStatus(
    string OrderId,
    string Status,
    long OrderAmountMinor,
    long CapturedAmountMinor,
    string Currency,
    string? MerchantReference,
    string? CaptureId
);
public sealed record KlarnaCapture(string CaptureId, long CapturedAmountMinor);

public interface IKlarnaGateway
{
    Task<KlarnaSession> StartAsync(KlarnaCheckoutRequest request, CancellationToken ct);
    Task<KlarnaHppStatus> ReadHppAsync(string sessionUrl, CancellationToken ct);
    Task<KlarnaOrderStatus> ReadOrderAsync(string orderId, CancellationToken ct);
    Task<KlarnaCapture> CaptureAsync(string orderId, long amountMinor, string key, CancellationToken ct);
    Task<string> RefundAsync(string orderId, long amountMinor, string key, CancellationToken ct);
    Task CancelOrderAsync(string orderId, string key, CancellationToken ct);
    Task DisableHppAsync(string sessionUrl, CancellationToken ct);
}
