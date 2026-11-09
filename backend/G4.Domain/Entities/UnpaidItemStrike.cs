namespace G4.Domain.Entities;

/// <summary>
/// One "Unpaid Item Strike" added to a buyer when a seller cancels an order whose
/// payment window has expired. OrderId is unique so a retry can never add two strikes.
/// </summary>
public class UnpaidItemStrike
{
    public int Id { get; set; }
    public int BuyerId { get; set; }
    public int SellerId { get; set; }
    public int OrderId { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
