namespace G4.Domain.Entities;

public partial class Payment
{
    public string? ProviderOrderId { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string? IdempotencyKey { get; set; }
    // Hosted Payment Page identity and URLs are kept server-side for idempotent retries.
    // Do not persist Klarna authorization tokens or buyer financing details.
    public string? KlarnaSessionId { get; set; }
    public string? KlarnaSessionUrl { get; set; }
    public string? KlarnaRedirectUrl { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
