namespace G4.Domain.Entities;

public sealed class SellerSettlement
{
    public int Id { get; set; }
    public int SellerAccountId { get; set; }
    public int SellerId { get; set; }
    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal PlatformFeeAmount { get; set; }
    public decimal FixedFeeAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal ProcessingAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal FeeCreditAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "Processing";
    public DateTime ReleaseAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
}
