namespace G4.Application.Integrations;

public sealed record IntegrationLogEntry(int? OrderId, string CorrelationId, string Service,
    string Operation, string Mode, string? EntityId, int Attempt, string Outcome, int? HttpStatus,
    long DurationMs, string? ProviderReference, string? ErrorCode, string? ErrorSummary,
    int? NotificationId, int? Cycle, DateTime CreatedAt);

public interface IIntegrationLogWriter
{
    Task WriteAsync(IntegrationLogEntry entry, CancellationToken ct);
}
