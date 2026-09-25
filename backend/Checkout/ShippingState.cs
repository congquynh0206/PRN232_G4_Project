namespace backend.Checkout;

public static class ShippingState
{
    private static readonly Dictionary<(string, string), string> Allowed = new()
    {
        [("NotCreated", "LabelCreated")] = "LabelCreated",
        [("LabelCreated", "PickedUp")] = "PickedUp",
        [("PickedUp", "InTransit")] = "InTransit",
        [("InTransit", "OutForDelivery")] = "OutForDelivery",
        [("OutForDelivery", "Delivered")] = "Delivered",
        [("OutForDelivery", "DeliveryFailed")] = "DeliveryFailed",
        [("DeliveryFailed", "OutForDelivery")] = "OutForDelivery",
        [("DeliveryFailed", "ReturningToSender")] = "ReturningToSender",
        [("ReturningToSender", "ReturnedToSeller")] = "ReturnedToSeller"
    };

    public static bool TryNext(string current, string nextEvent, out string next) =>
        Allowed.TryGetValue((current, nextEvent), out next!);

    public static string Next(string current, string nextEvent) =>
        TryNext(current, nextEvent, out var next) ? next : throw new InvalidOperationException($"Cannot move from {current} to {nextEvent}");
}
