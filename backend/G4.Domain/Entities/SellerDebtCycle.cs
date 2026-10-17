namespace G4.Domain.Entities;
public sealed class SellerDebtCycle
{
    public long Id { get; set; }
    public int SellerAccountId { get; set; }
    public int Number { get; set; }
    public decimal OpeningBalance { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime WarningAt { get; set; }
    public DateTime? WarnedAt { get; set; }
    public DateTime? SuspendedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? WriteOffEligibleAt { get; set; }
    public int RecoveryGraceSeconds { get; set; }
    public DateTime? LastRecoveryAt { get; set; }
    public decimal LastObservedBalance { get; set; }
}
