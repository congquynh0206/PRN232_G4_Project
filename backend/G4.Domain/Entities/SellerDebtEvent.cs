namespace G4.Domain.Entities;
public sealed class SellerDebtEvent
{
    public long Id { get; set; }
    public int SellerAccountId { get; set; }
    public int CycleNumber { get; set; }
    public string EventType { get; set; } = "";
    public string EntryKey { get; set; } = "";
    public decimal Balance { get; set; }
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
