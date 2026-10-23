namespace G4.Domain.Entities;

public sealed class SellerDebtRepayment
{
    public long Id { get; set; }
    public int SellerAccountId { get; set; }
    public int CycleNumber { get; set; }
    public decimal Amount { get; set; }
    public decimal ExpectedDebt { get; set; }
    public decimal RemainingDebt { get; set; }
    public string Currency { get; set; } = "USD";
    public string RequestKey { get; set; } = "";
    public bool SimulateFailure { get; set; }
    public string Status { get; set; } = "";
    public string BankReferenceId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
