namespace G4.Contracts.Checkout;

public sealed record IntegrationLogQueryOptions(int? OrderId = null, string? Service = null,
    string? Outcome = null, DateTime? From = null, DateTime? To = null, int Page = 1, int PageSize = 20);
public sealed record IntegrationLogView(long Id, int? OrderId, string CorrelationId, string Service,
    string Operation, string Mode, string? EntityId, int Attempt, string Outcome, int? HttpStatus,
    long DurationMs, string? ProviderReference, string? ErrorCode, string? ErrorSummary,
    int? NotificationId, int? Cycle, DateTime CreatedAt);
