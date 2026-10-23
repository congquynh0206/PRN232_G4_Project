namespace G4.Contracts.Checkout;

public sealed record SellerFundView(
    int OrderId,
    string OrderStatus,
    string SettlementStatus,
    string Currency,
    decimal Processing,
    decimal OnHold,
    int SellerLevel,
    int HoldPolicyVersion,
    int HoldSeconds,
    DateTime? HoldStartedAt,
    DateTime? ReleaseAt,
    string? BlockReason,
    int? DisputeId,
    int? ReturnRequestId,
    DateTime? ResponseDueAt,
    string? ResponseKind
);

public sealed record SellerFundsPage(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<SellerFundView> Items
);
