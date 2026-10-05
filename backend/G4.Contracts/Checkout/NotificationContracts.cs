namespace G4.Contracts.Checkout;

public sealed record NotificationQuery(int? OrderId = null, string? EventType = null,
    string? Status = null, int Page = 1, int PageSize = 20);
public sealed record DiagnosticsPage<T>(int Page, int PageSize, int TotalCount, IReadOnlyList<T> Items);
public sealed record EmailNotificationView(int Id, int OrderId, string EventType, string Recipient,
    string? From, string Subject, string Status, int Attempts, int AttemptsInCycle, int Cycle,
    DateTime CreatedAt, DateTime? LastAttemptAt, DateTime? NextAttemptAt, DateTime? SentAt,
    DateTime? CapturedAt, string? LastErrorCode, string? LastErrorSummary);
public sealed record EmailNotificationDetail(EmailNotificationView Summary, string Body,
    string? HtmlBody, IReadOnlyList<IntegrationLogView> Attempts);
