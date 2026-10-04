namespace G4.Contracts.Checkout;

public sealed record ShipperShipmentCard(int Id, int OrderId, string Direction, string? TrackingNumber,
    string? Status, int DeliveryAttempts, int? ShipperId, DateTime? ClaimedAt, DateTime CreatedAt,
    string? PickupAddress, string? DeliveryAddress, decimal? TotalWeightKg);
public sealed record ShipperShipmentPage(int Page, int PageSize, int TotalCount, int PendingCount,
    int MineCount, IReadOnlyList<ShipperShipmentCard> Items);
public sealed record ProductWeightRequest(decimal WeightKg);
