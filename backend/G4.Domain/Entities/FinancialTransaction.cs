namespace G4.Domain.Entities;

public sealed class FinancialTransaction
{
    public long Id { get; set; }
    public int SellerAccountId { get; set; }
    public int? OrderId { get; set; }
    public int? SettlementId { get; set; }
    public int? PayoutId { get; set; }
    public int? RefundId { get; set; }
    public string Type { get; set; } = "";
    public string Bucket { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string EntryKey { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}
