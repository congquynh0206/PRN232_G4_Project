namespace backend.Models;

public sealed class Refund
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public int? ReturnRequestId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Reason { get; set; } = "";
    public string? ProviderRefundId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
