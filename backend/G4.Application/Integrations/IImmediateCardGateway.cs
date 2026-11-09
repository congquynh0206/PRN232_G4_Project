namespace G4.Application.Integrations;

public sealed record CardAuthorizationState(
    string Status,
    decimal Amount,
    string? AuthorizationId = null,
    string? CaptureId = null,
    decimal RefundedAmount = 0
);

public interface IImmediateCardGateway
{
    Task<CardAuthorizationState> GetAsync(string key, CancellationToken ct);
    Task<CardAuthorizationState> AuthorizeAsync(
        string key, decimal amount, string validatedCardOutcome, CancellationToken ct);
    Task<CardAuthorizationState> CaptureAsync(string key, CancellationToken ct);
    Task<CardAuthorizationState> VoidAsync(string key, CancellationToken ct);
    Task<CardAuthorizationState> RefundAsync(
        string key, CancellationToken ct, decimal? amount = null, string? refundKey = null);
}
