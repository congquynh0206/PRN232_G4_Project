namespace G4.Domain.Entities;

public sealed class DisputeDecision
{
    public int Id { get; set; }
    public int DisputeId { get; set; }
    public int OrderId { get; set; }
    public int AdminId { get; set; }
    public string Outcome { get; set; } = "";
    public string Responsibility { get; set; } = "";
    public string Reason { get; set; } = "";
    public string EvidenceEntryIdsJson { get; set; } = "[]";
    public string RequestKey { get; set; } = "";
    public string PricingFingerprint { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public decimal? RequestedAmount { get; set; }
    public decimal MaximumRefund { get; set; }
    public decimal BuyerRefund { get; set; }
    public decimal OutboundShippingRetained { get; set; }
    public decimal ReturnShippingCost { get; set; }
    public decimal BuyerShippingRecovery { get; set; }
    public decimal SubsidyReversal { get; set; }
    public decimal SellerGrossRefund { get; set; }
    public decimal FeeCredit { get; set; }
    public decimal SellerNetDebit { get; set; }
    public string? ShippingPayer { get; set; }
    public string Currency { get; set; } = "USD";
}
