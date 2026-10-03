namespace G4.Contracts.Checkout;

public sealed record PayoutRequest(decimal Amount, string Key, bool SimulateFailure = false);
public sealed record FundHoldRequest(string Reason, string[] EvidenceLinks);
public sealed record FundHoldResolutionRequest(bool ReleaseToSeller, string Reason);

public sealed record SellerLevelProgress(
    int CompletedOrders,
    decimal PositiveFeedbackRate,
    decimal OnTimeDeliveryRate,
    int NextLevel,
    int RequiredOrders,
    decimal RequiredFeedbackRate,
    decimal RequiredOnTimeRate);

public sealed record FinanceTransactionView(
    long Id,
    int? OrderId,
    string Type,
    string Bucket,
    decimal Amount,
    string Currency,
    string? Description,
    DateTime CreatedAt);

public sealed record PayoutView(
    int Id,
    decimal Amount,
    string Currency,
    string Status,
    string DestinationMasked,
    string? BankReferenceId,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public sealed record SellerFinanceSummary(
    int SellerId,
    int Level,
    string LevelName,
    string Status,
    decimal MonthlySalesLimit,
    decimal MonthlySalesAmount,
    int HoldDays,
    decimal Processing,
    decimal Available,
    decimal OnHold,
    decimal Negative,
    SellerLevelProgress LevelProgress,
    IReadOnlyList<FinanceTransactionView> Transactions,
    IReadOnlyList<PayoutView> Payouts);
