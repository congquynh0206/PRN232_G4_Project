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
    public decimal MonthlySalesAmount { get; set; }
    public DateTime SalesMonth { get; set; }
    public DateTime UpdatedAt { get; set; }
}
