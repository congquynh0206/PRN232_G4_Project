namespace G4.Domain.Entities;

public class Promotion
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Sale";
    public string FundingSource { get; set; } = "Seller";
    public int? SellerId { get; set; }
    public int CreatedById { get; set; }
    public int? LegacyCouponId { get; set; }
    public string? Code { get; set; }
    public decimal Value { get; set; }
    public bool IsPercent { get; set; }
    public bool FreeShipping { get; set; }
    public decimal? Cap { get; set; }
    public decimal MinSubtotal { get; set; }
    public int MinQuantity { get; set; } = 1;
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public bool IsPaused { get; set; }
    public bool AdminPaused { get; set; }
    public string? PauseReason { get; set; }
    public int Version { get; set; } = 1;
    public int? MaxUsage { get; set; }
    public int? MaxUsagePerBuyer { get; set; }
    public decimal? Budget { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<PromotionTarget> Targets { get; set; } = [];
    public List<PromotionTier> Tiers { get; set; } = [];
}
public class PromotionTarget
{
    public int Id { get; set; }
    public int PromotionId { get; set; }
    public int? ProductId { get; set; }
    public int? CategoryId { get; set; }
}
public class PromotionTier
{
    public int Id { get; set; }
    public int PromotionId { get; set; }
    public int MinQuantity { get; set; }
    public decimal Percent { get; set; }
}
public class PromotionUsage
{
    public int Id { get; set; }
    public int PromotionId { get; set; }
    public int OrderId { get; set; }
    public int BuyerId { get; set; }
    public decimal Amount { get; set; }
    public string State { get; set; } = "Reserved";
    public DateTime ReservedAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
}
public class OrderPromotionSnapshot
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int PromotionId { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string FundingSource { get; set; } = "";
    public string? Code { get; set; }
    public int Version { get; set; }
    public decimal Amount { get; set; }
    public string AllocationJson { get; set; } = "[]";
}
public class PromotionAudit
{
    public int Id { get; set; }
    public int PromotionId { get; set; }
    public int ActorId { get; set; }
    public string Action { get; set; } = "";
    public string? Reason { get; set; }
    public string ChangesJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}
