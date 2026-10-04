using System.Globalization;
using System.Text;

namespace G4.Domain.Rules;

public static class ShippingPricing
{
    public static decimal Calculate(decimal kilograms, bool hanoiLocal)
    {
        if (kilograms is <= 0 or > 10_000_000m) throw new ArgumentOutOfRangeException(nameof(kilograms));
        var fee = hanoiLocal ? 2m + Math.Max(0m, kilograms - 1m) * .5m : 5m + Math.Max(0m, kilograms - 1m);
        return decimal.Round(fee, 2, MidpointRounding.AwayFromZero);
    }

    public static bool IsHanoiLocal(string? pickupState, string? pickupCountry, string? deliveryState, string? deliveryCountry) =>
        Normalize(pickupState) == "hanoi" && Normalize(deliveryState) == "hanoi" &&
        IsVietnam(pickupCountry) && IsVietnam(deliveryCountry);

    private static bool IsVietnam(string? country) => Normalize(country) is "vietnam" or "vn" or "vnm";
    private static string Normalize(string? value) => new((value ?? "").Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
        .Select(char.ToLowerInvariant).ToArray());
}
