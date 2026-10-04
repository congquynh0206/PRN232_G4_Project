namespace G4.Domain.Entities;

public partial class ShippingInfo
{
    public string Direction { get; set; } = "Outbound";
    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? FailureReason { get; set; }
    public string? IdempotencyKey { get; set; }
    public int DeliveryAttempts { get; set; }
    public int? ShipperId { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public string? PickupAddressSnapshot { get; set; }
    public string? DeliveryAddressSnapshot { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
