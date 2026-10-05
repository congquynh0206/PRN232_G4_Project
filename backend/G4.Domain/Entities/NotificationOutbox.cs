namespace G4.Domain.Entities;

public sealed class NotificationOutbox
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string EventType { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string? HtmlBody { get; set; }
    public string? From { get; set; }
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    public int AttemptsInCycle { get; set; }
    public int Cycle { get; set; } = 1;
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorSummary { get; set; }
    public DateTime? ProcessingUntil { get; set; }
    public Guid? ProcessingToken { get; set; }
    public DateTime? CapturedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
}
