namespace G4.Domain.Entities;

public partial class Dispute
{
    public bool WorkflowEnabled { get; set; }
    public bool IsOpen { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime SellerResponseDueAt { get; set; }
    public DateTime? SellerRespondedAt { get; set; }
    public DateTime? BuyerResponseDueAt { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? Proposal { get; set; }
    public string? Outcome { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
