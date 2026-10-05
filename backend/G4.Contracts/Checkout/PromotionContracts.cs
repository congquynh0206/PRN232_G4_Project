namespace G4.Contracts.Checkout;

public record PromotionInput
{
    public string Name { get; init; } = "";
    public string Type { get; init; } = "Sale";
    public string? Code { get; init; }
    public int? SellerId { get; init; }
    public decimal Value { get; init; }
    public bool IsPercent { get; init; }
    public bool FreeShipping { get; init; }
    public decimal? Cap { get; init; }
    public decimal MinSubtotal { get; init; }
    public int MinQuantity { get; init; } = 1;
    public DateTime StartAt { get; init; }
    public DateTime EndAt { get; init; }
    public int? MaxUsage { get; init; }
    public int? MaxUsagePerBuyer { get; init; }
    public decimal? Budget { get; init; }
    public int Version { get; init; }
    public bool IsPaused { get; init; }
    public int[] ProductIds { get; init; } = [];
    public int[] CategoryIds { get; init; } = [];
    public PromotionTierInput[] Tiers { get; init; } = [];
}
public record PromotionTierInput(int MinQuantity, decimal Percent);
public record PromotionStateInput(bool Paused, string? Reason, int Version);
public record PromotionView : PromotionInput
{
    public int Id { get; init; }
    public string FundingSource { get; init; } = "Seller";
    public bool AdminPaused { get; init; }
    public string? PauseReason { get; init; }
    public string Status { get; init; } = "Scheduled";
    public int UsedCount { get; init; }
    public int ReservedCount { get; init; }
    public decimal Spent { get; init; }
    public decimal ReservedAmount { get; init; }
}
public record PromotionCounts(int All, int Scheduled, int Active, int Paused, int Ended);
public record PromotionPage(int Page, int PageSize, int TotalCount, PromotionCounts Counts, IReadOnlyList<PromotionView> Items);
