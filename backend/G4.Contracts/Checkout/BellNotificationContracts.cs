namespace G4.Contracts.Checkout;

public sealed record BellNotificationView(
    long Id,
    string Title,
    string Message,
    string TargetUrl,
    DateTime CreatedAt,
    DateTime? ReadAt
);

public sealed record BellNotificationPage(
    int Page,
    int PageSize,
    int TotalCount,
    int UnreadCount,
    long LatestId,
    IReadOnlyList<BellNotificationView> Items
);

public sealed record ReadAllNotificationsRequest(long ThroughId);
