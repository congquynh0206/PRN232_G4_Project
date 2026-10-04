namespace G4.Contracts.Checkout;

public sealed record BuyerOrderCard(int Id, DateTime? OrderDate, DateTime UpdatedAt, string? Status,
    decimal? TotalPrice, string Currency, string? ProductTitle, string? ImageUrl, string? SellerName,
    int ProductCount, int ItemCount, bool HasRefund, string? Attention, SellerOrderCaseBadge? Dispute);
public sealed record BuyerOrderListPage(int Page, int PageSize, int TotalCount,
    IReadOnlyDictionary<string, int> Counts, IReadOnlyList<BuyerOrderCard> Items);
