namespace G4.Domain.Entities;

public sealed class PayPalCheckout
{
    public long Id { get; set; }
    public int BuyerId { get; set; }
    public string CheckoutKey { get; set; } = "";
    public string RequestKey { get; set; } = "";
    public string Status { get; set; } = "Creating";
    public string? ProviderOrderId { get; set; }
    public string? ApprovalUrl { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public partial class Payment
{
    public long? PayPalCheckoutId { get; set; }
    public DateTime? CaptureReservedAt { get; set; }
    public decimal? ReservedSellerGross { get; set; }
}
