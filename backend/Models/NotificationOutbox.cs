namespace backend.Models;

public sealed class NotificationOutbox
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string EventType { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
}
