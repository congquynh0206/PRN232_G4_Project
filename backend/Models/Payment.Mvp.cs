namespace backend.Models;

public partial class Payment
{
    public string? ProviderOrderId { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
