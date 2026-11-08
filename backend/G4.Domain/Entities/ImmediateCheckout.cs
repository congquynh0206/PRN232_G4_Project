namespace G4.Domain.Entities;

public partial class Product
{
    public bool ImmediatePaymentRequired { get; set; }
    public bool IsListed { get; set; } = true;
}

public partial class OrderTable
{
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool ImmediatePaymentInProgress { get; set; }
}

// A quote is not an inventory reservation or an unpaid order.
public sealed class ImmediateCheckout
{
    public Guid Id { get; set; }
    public int BuyerId { get; set; }
    public string RequestJson { get; set; } = "";
    public string QuoteJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Status { get; set; } = "Ready";
    public int? OrderId { get; set; }
}

public sealed class ImmediatePaymentAttempt
{
    public long Id { get; set; }
    public Guid CheckoutId { get; set; }
    public string Key { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public int? OrderId { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAt { get; set; }
}
