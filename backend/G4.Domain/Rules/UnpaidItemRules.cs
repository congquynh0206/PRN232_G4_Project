namespace G4.Domain.Rules;

/// <summary>
/// Rules for "Buy It Now" orders that are created first and paid later.
/// The buyer holds the item until PaymentExpiresAt; after that the seller may cancel the
/// order, which gives the buyer one Unpaid Item Strike and puts the stock back on sale.
/// </summary>
public static class UnpaidItemRules
{
    public const string CancelReasonCode = "UnpaidItem";
    public const int DefaultHoldMinutes = 15;
    public const int DefaultStrikeLimit = 2;
    public const int DefaultStrikeWindowMinutes = 30 * 24 * 60;

    public static int NormalizeHoldMinutes(int? configured) =>
        configured is > 0 and <= 10 * 24 * 60 ? configured.Value : DefaultHoldMinutes;

    public static int NormalizeStrikeLimit(int? configured) =>
        configured is >= 1 and <= 10 ? configured.Value : DefaultStrikeLimit;

    public static int NormalizeStrikeWindowMinutes(int? configured) =>
        configured is > 0 ? configured.Value : DefaultStrikeWindowMinutes;

    /// <summary>The seller button opens only after the hold deadline has passed.</summary>
    public static bool CanSellerCancel(string? status, DateTime? paymentExpiresAt, DateTime nowUtc) =>
        status == "AwaitingPayment" && paymentExpiresAt != null && paymentExpiresAt <= nowUtc;

    /// <summary>Consecutive strikes inside the window reach the limit: ban the account.</summary>
    public static bool ShouldBan(int strikesInWindow, int limit) => strikesInWindow >= limit;
}
