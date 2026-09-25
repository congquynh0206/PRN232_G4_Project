namespace backend.Models;

public sealed class ShippingEvent
{
    public int Id { get; set; }
    public int ShippingInfoId { get; set; }
    public string ExternalEventId { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Location { get; set; }
    public string? Note { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime ReceivedAt { get; set; }
}
