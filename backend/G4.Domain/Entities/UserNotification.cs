namespace G4.Domain.Entities;

public sealed class UserNotification
{
    public long Id { get; set; }
    public long CommerceChangeId { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string TargetUrl { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
