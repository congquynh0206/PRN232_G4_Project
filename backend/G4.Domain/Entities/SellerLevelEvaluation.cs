namespace G4.Domain.Entities;

public sealed class SellerLevelEvaluation
{
    public long Id { get; set; }
    public int SellerId { get; set; }
    public int PreviousLevel { get; set; }
    public int ResultLevel { get; set; }
    public string Source { get; set; } = "";
    public string Reason { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class SellerOrderFeedback
{
    public long Id { get; set; }
    public int OrderId { get; set; }
    public int BuyerId { get; set; }
    public int SellerId { get; set; }
    public int Rating { get; set; }
    public string Comment { get; set; } = "";
    public string RequestKey { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
