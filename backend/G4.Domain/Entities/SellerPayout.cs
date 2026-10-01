namespace G4.Domain.Entities;

public sealed class SellerPayout
{
    public int Id { get; set; }
    public int SellerAccountId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "Created";
    public string IdempotencyKey { get; set; } = "";
    public string DestinationMasked { get; set; } = "****1234";
    public string? BankReferenceId { get; set; }
    public bool SimulateFailure { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
