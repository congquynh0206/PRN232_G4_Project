using G4.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Returns;

public static class ReturnWindowPolicy
{
    public const int LegacySeconds = 604800;

    public static void SnapshotDelivery(
        OrderTable order,
        DateTime deliveredAt,
        IConfiguration? config
    )
    {
        if (order.ReturnWindowClosesAt != null)
            return;
        var seconds = Math.Clamp(
            config?.GetValue("Returns:WindowSeconds", LegacySeconds) ?? LegacySeconds,
            1,
            31536000
        );
        order.ReturnWindowPolicyVersion = 1;
        order.ReturnWindowSeconds = seconds;
        order.ReturnWindowStartedAt = DateTime.SpecifyKind(deliveredAt, DateTimeKind.Utc);
        order.ReturnWindowClosesAt = order.ReturnWindowStartedAt.Value.AddSeconds(seconds);
    }

    // Imported/legacy deliveries without a snapshot retain the previous seven-day entitlement.
    // Never infer Development's five-minute policy from today's configuration.
    public static DateTime? Deadline(OrderTable order, DateTime? deliveredAt) =>
        order.ReturnWindowClosesAt ?? deliveredAt?.AddSeconds(LegacySeconds);

    public static bool IsOpen(OrderTable order, DateTime? deliveredAt, DateTime now) =>
        deliveredAt != null
        && now >= (order.ReturnWindowStartedAt ?? deliveredAt.Value)
        && now < Deadline(order, deliveredAt);

    public static bool TimelyReturn(
        OrderTable order,
        DateTime? deliveredAt,
        DateTime? requestedAt
    ) => requestedAt != null && IsOpen(order, deliveredAt, requestedAt.Value);
}
