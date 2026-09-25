namespace backend.Models;

public partial class ShippingInfo
{
    public string Direction { get; set; } = "Outbound";
    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? FailureReason { get; set; }
    public string? IdempotencyKey { get; set; }
    public int DeliveryAttempts { get; set; }
}
