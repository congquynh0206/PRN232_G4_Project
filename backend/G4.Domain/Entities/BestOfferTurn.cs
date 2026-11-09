namespace G4.Domain.Entities;

public sealed class BestOfferTurn
{
    public long Id { get; set; }
    public long BestOfferId { get; set; }
    public int ActorId { get; set; }
    public decimal Amount { get; set; }
    public string Action { get; set; } = "Offer";
    public string? Result { get; set; }
    public string? RequestKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public BestOffer? BestOffer { get; set; }
}
