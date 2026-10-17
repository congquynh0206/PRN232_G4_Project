namespace G4.Domain.Entities;

public partial class Review
{
    public int? OrderId { get; set; }
    public string? RequestKey { get; set; }
}

public sealed class ProductReviewReply
{
    public int Id { get; set; }
    public int ReviewId { get; set; }
    public int SellerId { get; set; }
    public string Comment { get; set; } = "";
    public string RequestKey { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
