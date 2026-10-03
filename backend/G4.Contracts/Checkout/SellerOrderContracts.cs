namespace G4.Contracts.Checkout;

public sealed record SellerOrderCaseBadge(int Id, string Status, bool IsOpen, string? Outcome);
public sealed record SellerOrderCard(int Id, DateTime? OrderDate, DateTime UpdatedAt, string? Status,
    decimal? TotalPrice, string Currency, string? ProductTitle, string? BuyerName,
    int ProductCount, int ItemCount, bool HasRefund, string? Attention, SellerOrderCaseBadge? Dispute);
public sealed record SellerOrderListPage(int Page, int PageSize, int TotalCount,
    IReadOnlyDictionary<string, int> Counts, int ActionCount, IReadOnlyList<SellerOrderCard> Items);
