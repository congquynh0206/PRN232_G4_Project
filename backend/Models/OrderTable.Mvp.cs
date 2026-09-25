namespace backend.Models;

public partial class OrderTable
{
    public int? SellerId { get; set; }
    public decimal Subtotal { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal DiscountAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime? PaymentExpiresAt { get; set; }
    public string? AddressSnapshot { get; set; }
    public string? CouponCode { get; set; }
    public string? CheckoutKey { get; set; }
    public string? CancelPreviousStatus { get; set; }
    public string? CancelDecisionReason { get; set; }
}
