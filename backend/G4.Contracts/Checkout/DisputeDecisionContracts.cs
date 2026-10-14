namespace G4.Contracts.Checkout;

public sealed record AdminDecisionPreviewInput(string Outcome, string Responsibility, decimal? RefundAmount);
public sealed record AdminDecisionInput(string Outcome, string Responsibility, decimal? RefundAmount,
    string Reason, int[] EvidenceEntryIds, string Key, string PricingFingerprint);
public sealed record AdminDecisionQuote(int DisputeId, int OrderId, string Outcome, string Responsibility,
    decimal MaximumRefund, decimal BuyerRefund, decimal OutboundShippingRetained, decimal ReturnShippingCost,
    decimal BuyerShippingRecovery, decimal SubsidyReversal, decimal SellerGrossRefund, decimal FeeCredit,
    decimal SellerNetDebit, string? ShippingPayer, string Currency, string PricingFingerprint);
public sealed record AdminDecisionView(int Id, int AdminId, string AdminName, string Outcome, string Responsibility,
    string Reason, int[] EvidenceEntryIds, DateTime CreatedAt, decimal MaximumRefund, decimal BuyerRefund,
    decimal OutboundShippingRetained, decimal ReturnShippingCost, decimal BuyerShippingRecovery,
    decimal SubsidyReversal, decimal SellerGrossRefund, decimal FeeCredit, decimal SellerNetDebit,
    string? ShippingPayer, string Currency, string? RefundStatus, decimal? ActualRefund);
