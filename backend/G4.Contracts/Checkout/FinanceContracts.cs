namespace G4.Contracts.Checkout;
public sealed record PayoutRequest(decimal Amount, string Key, bool SimulateFailure = false);
public sealed record FundHoldRequest(string Reason, string[] EvidenceLinks);
public sealed record FundHoldResolutionRequest(bool ReleaseToSeller, string Reason);
public sealed record SellerLevelProgress(
    int CompletedOrders,
    decimal? PositiveFeedbackRate,
    decimal? OnTimeDeliveryRate,
    int NextLevel,
    int RequiredOrders,
    decimal RequiredFeedbackRate,
    decimal RequiredOnTimeRate
);
public sealed record FinanceTransactionView(
    long Id,
    int? OrderId,
    string Type,
    string Bucket,
    decimal Amount,
    string Currency,
    string? Description,
    DateTime CreatedAt
);
public sealed record PayoutView(
    int Id,
    decimal Amount,
    string Currency,
    string Status,
    string DestinationMasked,
    string? BankReferenceId,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    IReadOnlyList<PayoutStepView>? History = null,
    int WorkflowVersion = 0
);
public sealed record PayoutStepView(string? FromStatus, string ToStatus, DateTime OccurredAt, int? ActorUserId, string ActorRole, string? BankReferenceId);
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
    IReadOnlyList<PayoutView> Payouts,
    int HoldSeconds = 0,
    decimal PayoutPending = 0,
    decimal PaidOut = 0
);

public sealed record PlatformFeeChangeRequest(decimal FeePercent, decimal FixedFee, DateTimeOffset? EffectiveAt,
    string Reason, string RequestKey, int ExpectedLatestPolicyId);
public sealed record PlatformFeePolicyView(int Id, decimal FeePercent, decimal FixedFee,
    decimal PreviousFeePercent, decimal PreviousFixedFee, DateTime EffectiveAt, DateTime CreatedAt,
    int? ChangedById, string ChangedBy, string Reason);
public sealed record PlatformFeeSettings(PlatformFeePolicyView Current, int LatestPolicyId,
    int Page, int PageSize, int TotalCount, IReadOnlyList<PlatformFeePolicyView> Items);
