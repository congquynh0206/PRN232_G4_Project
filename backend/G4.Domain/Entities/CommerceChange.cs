namespace G4.Domain.Entities;

// Routing identifiers and safe business summaries; no amounts, evidence or identity values.
public sealed class CommerceChange
{
    public long Id { get; set; }
    public string Topic { get; set; } = "";
    public int? OrderId { get; set; }
    public int? SellerId { get; set; }
    public int? BuyerId { get; set; }
    public int? SellerAccountId { get; set; }
    public int? ShippingInfoId { get; set; }
    public int? ShipperId { get; set; }
    public int? DisputeId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public string? NotificationTitle { get; set; }
    public string? NotificationMessage { get; set; }
}
