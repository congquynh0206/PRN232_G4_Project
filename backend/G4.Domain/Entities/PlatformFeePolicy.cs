namespace G4.Domain.Entities;

// Each row is an immutable configuration change and its audit record.
public sealed class PlatformFeePolicy
{
    public int Id { get; set; }
    public decimal FeePercent { get; set; }
    public decimal FixedFee { get; set; }
    public decimal PreviousFeePercent { get; set; }
    public decimal PreviousFixedFee { get; set; }
    public DateTime EffectiveAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? ChangedById { get; set; }
    public string Reason { get; set; } = "";
    public string RequestKey { get; set; } = "";
}
