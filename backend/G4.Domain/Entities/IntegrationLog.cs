namespace G4.Domain.Entities;

public sealed class IntegrationLog
{
    public long Id { get; set; }
    public int? OrderId { get; set; }
    public string CorrelationId { get; set; } = "";
    public string Service { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Mode { get; set; } = "";
    public string? EntityId { get; set; }
    public int Attempt { get; set; }
    public string Outcome { get; set; } = "";
    public int? HttpStatus { get; set; }
    public long DurationMs { get; set; }
    public string? ProviderReference { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorSummary { get; set; }
    public int? NotificationId { get; set; }
    public int? Cycle { get; set; }
    public DateTime CreatedAt { get; set; }
}
