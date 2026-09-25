namespace backend.Checkout;

public static class OrderState
{
    private static readonly Dictionary<(string, string), string> Allowed = new()
    {
        [("AwaitingPayment", "PaymentSucceeded")] = "Paid",
        [("AwaitingPayment", "Cancel")] = "Cancelled",
        [("AwaitingPayment", "Expire")] = "Expired",
        [("Paid", "Prepare")] = "Preparing",
        [("Paid", "RequestCancel")] = "CancelRequested",
        [("Preparing", "RequestCancel")] = "CancelRequested",
        [("CancelRequested", "AcceptCancel")] = "Cancelled",
        [("CancelRequested", "RejectCancel")] = "Preparing",
        [("Preparing", "Ship")] = "Shipping",
        [("Shipping", "Deliver")] = "Delivered",
        [("Delivered", "Close")] = "Closed"
    };

    public static bool TryNext(string current, string action, out string next) =>
        Allowed.TryGetValue((current, action), out next!);

    public static string Next(string current, string action) =>
        TryNext(current, action, out var next) ? next : throw new InvalidOperationException($"Cannot {action} from {current}");
}
