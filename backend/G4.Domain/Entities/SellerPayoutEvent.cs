namespace G4.Domain.Entities;

public sealed class SellerPayoutEvent
{
    public long Id { get; set; }
    public int PayoutId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = "";
    public int? ActorUserId { get; set; }
    public string ActorRole { get; set; } = "system";
    public DateTime OccurredAt { get; set; }
    public string? BankReferenceId { get; set; }
}
