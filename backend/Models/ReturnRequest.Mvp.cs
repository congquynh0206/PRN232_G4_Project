namespace backend.Models;

public partial class ReturnRequest
{
    public DateTime? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }
    public DateTime? ReturnDeadline { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public int? RefundId { get; set; }
}
