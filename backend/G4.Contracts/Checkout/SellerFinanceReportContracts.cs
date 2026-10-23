namespace G4.Contracts.Checkout;

public sealed record SellerReportSource(
    long TransactionId,
    int? OrderId,
    int? RefundId,
    string Type,
    decimal Amount,
    DateTime OccurredAt
);

public sealed record SellerReportPeriod(
    DateOnly Period,
    int PaidOrders,
    decimal BuyerPaid,
    decimal BuyerRefunded,
    decimal PlatformFee,
    decimal Net,
    decimal PlatformSubsidy,
    decimal SubsidyReversal,
    decimal ShippingAdjustments,
    decimal ReturnShippingCost,
    decimal ReturnShippingRecovery,
    IReadOnlyList<SellerReportSource>? Sources = null
);

public sealed record SellerFinanceReport(
    DateOnly From,
    DateOnly To,
    string TimeZone,
    string Group,
    SellerReportPeriod Totals,
    IReadOnlyList<SellerReportPeriod> Chart,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<SellerReportPeriod> Items,
    int UnreconciledPayments,
    int UnreconciledRefunds
);
