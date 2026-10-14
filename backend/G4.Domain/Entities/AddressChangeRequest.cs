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
}

public sealed class SellerShippingPreference
{
    public int SellerId { get; set; }
    public decimal AddressChangeCap { get; set; }
}
