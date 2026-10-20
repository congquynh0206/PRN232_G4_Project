namespace G4.Domain.Entities;
public sealed class AddressChangeRequest
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string RequestKey { get; set; } = "";
    public string Reason { get; set; } = "";
    public string OldAddress { get; set; } = "";
    public string NewAddress { get; set; } = "";
    public string NewState { get; set; } = "";
    public string NewCountry { get; set; } = "";
    public decimal OldShippingCost { get; set; }
    public decimal NewShippingCost { get; set; }
    public decimal SellerCap { get; set; }
    public decimal BuyerRefund { get; set; }
    public int? RefundId { get; set; }
    public string Status { get; set; } = "Pending";
    public string? DecisionReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public int WorkflowVersion { get; set; }
    public bool IsPending { get; set; }
    public string? QuoteToken { get; set; }
    public int CapRevision { get; set; }
    public int? BuyerUserId { get; set; }
    public DateTime? BuyerConfirmedAt { get; set; }
    public int? SellerUserId { get; set; }
    public DateTime? SellerConfirmedAt { get; set; }
    public int? ExpectedShipperId { get; set; }
    public int? ShipperUserId { get; set; }
    public DateTime? ShipperDecidedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? AppliedAt { get; set; }
}

public sealed class SellerShippingPreference
{
    public int SellerId { get; set; }
    public decimal AddressChangeCap { get; set; }
    public int Revision { get; set; }
}
