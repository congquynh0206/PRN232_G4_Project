using G4.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Shipping;

// Seller handling time: from the moment an order becomes Paid until the outbound label is
// created. This slice only records and displays the result; being late has no penalty yet.
public static class ShippingHandlingPolicy
{
    public const int DefaultHandlingSeconds = 172800;
    public const int DefaultTransitSeconds = 259200;
    private const int MaxSeconds = 31536000;

    public static int HandlingSeconds(IConfiguration? config) =>
        Math.Clamp(
            config?.GetValue("Shipping:HandlingSeconds", DefaultHandlingSeconds)
                ?? DefaultHandlingSeconds,
            1,
            MaxSeconds
        );

    public static int TransitSeconds(IConfiguration? config) =>
        Math.Clamp(
            config?.GetValue("Shipping:TransitSeconds", DefaultTransitSeconds)
                ?? DefaultTransitSeconds,
            1,
            MaxSeconds
        );

    // Called from SaveChanges for every order that has just become Paid. Orders created
    // before the upgrade have no HandlingSeconds snapshot and never receive a deadline.
    public static void StartClock(OrderTable order, DateTime paidAt)
    {
        if (order.HandlingSeconds is not { } seconds || order.HandlingDeadlineAt != null)
            return;
        order.HandlingDeadlineAt = DateTime
            .SpecifyKind(paidAt, DateTimeKind.Utc)
            .AddSeconds(seconds);
    }

    public static void RecordHandover(OrderTable order, DateTime labelCreatedAt) =>
        order.HandedOverAt ??= DateTime.SpecifyKind(labelCreatedAt, DateTimeKind.Utc);

    // The promise is anchored to the handling deadline, so shipping late does not move it.
    public static DateTime EstimatedArrival(
        OrderTable order,
        DateTime labelCreatedAt,
        IConfiguration? config
    ) =>
        DateTime
            .SpecifyKind(order.HandlingDeadlineAt ?? labelCreatedAt, DateTimeKind.Utc)
            .AddSeconds(TransitSeconds(config));

    public static bool? HandedOverOnTime(OrderTable order) =>
        order.HandlingDeadlineAt == null || order.HandedOverAt == null
            ? null
            : order.HandedOverAt <= order.HandlingDeadlineAt;
}
