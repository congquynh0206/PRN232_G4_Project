namespace G4.Domain.Entities;
public sealed class SellerAccount
{
    public int Id { get; set; }
    public int SellerId { get; set; }
    public int Level { get; set; } = 1;
    public string Status { get; set; } = "Active";
    public decimal MonthlySalesLimit { get; set; }
    public int HoldDays { get; set; }
    public decimal ProcessingBalance { get; set; }
    public decimal AvailableBalance { get; set; }
    public decimal OnHoldBalance { get; set; }
    public decimal NegativeBalance { get; set; }
    public int DebtCycleNumber { get; set; }
    public DateTime? DebtStartedAt { get; set; }
    public DateTime? DebtDueAt { get; set; }
    public bool DebtSuspended { get; set; }
    public DateTime? PermanentSalesBlockedAt { get; set; }
    public long? CurrentIdentityId { get; set; }
    public int IdentityVersion { get; set; }
    public DateTime? LevelActivityStartedAt { get; set; }
    public DateTime? LastLevelEvaluatedAt { get; set; }
    public decimal MonthlySalesAmount { get; set; }
    public DateTime SalesMonth { get; set; }
    public DateTime UpdatedAt { get; set; }
}
