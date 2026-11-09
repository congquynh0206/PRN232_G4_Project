namespace G4.Domain.Entities;

public sealed class BestOffer
{
    public long Id { get; set; }
    public int ProductId { get; set; }
    public int BuyerId { get; set; }
    public int SellerId { get; set; }
    public int AddressId { get; set; }
    public decimal CurrentAmount { get; set; }
    public int CurrentActorId { get; set; }
    public string Status { get; set; } = "PendingSeller";
    public int BuyerOfferCount { get; set; }
    public string PaymentMethod { get; set; } = "Card";
    public string? CardProfile { get; set; }
    public string? CardExpiry { get; set; }
    public int? OrderId { get; set; }
    public string CreationKey { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
