namespace G4.Domain.Entities;

public partial class ReturnRequest
{
    public int PricingPolicyVersion { get; set; }
    public string? ReasonCode { get; set; }
    public string? Description { get; set; }
    public string? RequestKey { get; set; }
    public string? PricingFingerprint { get; set; }
    public string? ShippingPayer { get; set; }
    public decimal GoodsPaidSnapshot { get; set; }
    public decimal OutboundShippingRetained { get; set; }
    public decimal ReturnShippingFee { get; set; }
    public decimal BuyerReturnShippingFee { get; set; }
    public decimal BuyerRefundAmount { get; set; }
    public decimal PlatformSubsidyReversal { get; set; }
    public decimal SellerGrossRefund { get; set; }
    public decimal? WeightKgSnapshot { get; set; }
    public string? PickupAddressSnapshot { get; set; }
    public string? DeliveryAddressSnapshot { get; set; }
    public DateTime? ShippingCostChargedAt { get; set; }
}
