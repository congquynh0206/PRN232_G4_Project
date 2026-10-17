namespace G4.Contracts.Checkout;

public sealed record SellerFeedbackInput(int Rating, string? Comment, string Key);
public sealed record SellerFeedbackView(long Id, int OrderId, int Rating, string Comment, DateTime CreatedAt);
public sealed record SellerLevelMetrics(int CompletedOrders, int PaidOrders, int FeedbackCount,
    decimal? PositiveFeedbackRate, int DeliveryCount, decimal? OnTimeDeliveryRate,
    decimal CancellationRate, decimal ReturnRate, decimal DisputeRate, int ActivityDays,
    decimal NegativeBalance, bool Restricted, DateTime WindowStart, DateTime WindowEnd);
public sealed record SellerLevelTier(int Level, int CompletedOrders, decimal PositiveFeedbackRate,
    decimal OnTimeDeliveryRate, int ActivityDays, decimal MaxCancellationRate, decimal MaxReturnRate, decimal MaxDisputeRate);
public sealed record SellerLevelPolicy(int WindowDays, int MinimumPaidOrders, int EvaluationSeconds,
    SellerLevelTier Level2, SellerLevelTier Level3);
public sealed record SellerLevelCriterion(string Code, string Label, decimal? Actual, decimal Required, bool Maximum, bool? Passed);
public sealed record SellerLevelTierProgress(int Level, bool Eligible, IReadOnlyList<SellerLevelCriterion> Criteria);
public sealed record SellerLevelDecision(int PreviousLevel, int ResultLevel, string Reason,
    SellerLevelMetrics Metrics, SellerLevelPolicy Policy, IReadOnlyList<SellerLevelTierProgress> Tiers);
public sealed record SellerLevelHistory(long Id, string Source, DateTime CreatedAt, SellerLevelDecision Decision);
public sealed record SellerLevelDetails(int SellerId, int Level, DateTime? LastEvaluatedAt,
    DateTime? NextEvaluationAt, SellerLevelDecision Preview, IReadOnlyList<SellerLevelHistory> History);
public sealed record SellerLevelSimulation(int CurrentLevel, SellerLevelMetrics Metrics);
