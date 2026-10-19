namespace G4.Contracts.Checkout;

public sealed record AdminOrderLookupQuery(
    string? Search = null,
    string? Buyer = null,
    string? Seller = null,
    DateTime? From = null,
    DateTime? To = null,
    string? Status = null,
    string? PaymentStatus = null,
    string? RefundStatus = null,
    string? QuickFilter = null,
    int Page = 1,
    int PageSize = 20,
    int? OrderId = null,
    string SearchType = "Auto"
);

public sealed record AdminOrderLookupRow(
    int OrderId,
    DateTime? OrderDate,
    string? Buyer,
    string? Seller,
    decimal Total,
    string? OrderStatus,
    string PaymentStatus,
    string RefundStatus,
    decimal RefundedAmount,
    string? ShippingStatus,
    bool HasPaymentFailure,
    bool HasRefund,
    bool HasDispute
);

public sealed record AdminOrderLookupPage(int Page, int PageSize, int TotalCount, IReadOnlyList<AdminOrderLookupRow> Items);

public sealed record AdminOrderPaymentView(
    int PaymentId,
    string? Method,
    string? Status,
    decimal Amount,
    DateTime CreatedAt,
    DateTime? PaidAt,
    string? CaptureReference,
    string? ProviderOrderReference,
    string? ErrorCode
);

public sealed record AdminOrderRefundView(
    int RefundId,
    int PaymentId,
    decimal Amount,
    string Currency,
    string Reason,
    string Status,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? ProviderReference
);

public sealed record AdminOrderShipmentEventView(string Status, DateTime OccurredAt);
public sealed record AdminOrderShipmentView(
    int ShipmentId,
    string Direction,
    string? Carrier,
    string? TrackingReference,
    string? Status,
    DateTime CreatedAt,
    DateTime? DeliveredAt,
    IReadOnlyList<AdminOrderShipmentEventView> Events
);
public sealed record AdminOrderDisputeView(int DisputeId, string? Status, DateTime CreatedAt);
public sealed record AdminOrderItemView(string? Title, int Quantity, decimal UnitPrice);

public sealed record AdminOrderLookupDetail(
    AdminOrderLookupRow Order,
    decimal CollectedAmount,
    decimal RefundedAmount,
    decimal RemainingAmount,
    IReadOnlyList<AdminOrderItemView> Items,
    IReadOnlyList<AdminOrderPaymentView> Payments,
    IReadOnlyList<AdminOrderRefundView> Refunds,
    IReadOnlyList<AdminOrderShipmentView> Shipments,
    IReadOnlyList<AdminOrderDisputeView> Disputes
);
