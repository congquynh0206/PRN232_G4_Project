namespace G4.Domain.Rules;

/// <summary>Route class between the pickup and delivery address.</summary>
public enum ShippingZone
{
    SameProvince,
    SameArea,
    CrossArea,
    UsSameState,
    UsCrossState,
    International,
}

/// <summary>
/// Zone price table, charged on the actual fractional weight (no rounding up to whole kg):
/// fee = base + max(0, kg - 1) × perKg, rounded to 0.01 USD at the end.
/// </summary>
public static class ShippingPricing
{
    public static (decimal Base, decimal PerKg) Rate(ShippingZone zone) =>
        zone switch
        {
            ShippingZone.SameProvince => (2.00m, 0.50m),
            ShippingZone.SameArea => (3.50m, 0.75m),
            ShippingZone.CrossArea => (5.00m, 1.00m),
            ShippingZone.UsSameState => (4.00m, 0.75m),
            ShippingZone.UsCrossState => (6.00m, 1.00m),
            ShippingZone.International => (15.00m, 5.00m),
            _ => throw new ArgumentOutOfRangeException(nameof(zone)),
        };

    public static decimal Calculate(decimal kilograms, ShippingZone zone)
    {
        if (kilograms is <= 0 or > 10_000_000m)
            throw new ArgumentOutOfRangeException(nameof(kilograms));
        var (baseFee, perKg) = Rate(zone);
        var fee = baseFee + Math.Max(0m, kilograms - 1m) * perKg;
        return decimal.Round(fee, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Two-rate shortcut kept for callers that only know same/other province.</summary>
    public static decimal Calculate(decimal kilograms, bool sameProvince) =>
        Calculate(kilograms, sameProvince ? ShippingZone.SameProvince : ShippingZone.CrossArea);

    /// <summary>
    /// Classifies a route. Unsupported countries or unknown Vietnamese provinces fall back to
    /// the cross-area rate unless both sides spell the same province.
    /// </summary>
    public static ShippingZone Zone(
        string? pickupState,
        string? pickupCountry,
        string? deliveryState,
        string? deliveryCountry
    )
    {
        var from = AddressRegions.Country(pickupCountry);
        var to = AddressRegions.Country(deliveryCountry);
        if (from is null || to is null)
            return ShippingZone.CrossArea;
        if (from != to)
            return ShippingZone.International;
        if (from == AddressRegions.UnitedStates)
        {
            var a = AddressRegions.State(from, pickupState);
            var b = AddressRegions.State(to, deliveryState);
            return a is not null && a == b ? ShippingZone.UsSameState : ShippingZone.UsCrossState;
        }
        var pickup = AddressRegions.Province(pickupState);
        var delivery = AddressRegions.Province(deliveryState);
        if (pickup is null || delivery is null)
        {
            var key = AddressRegions.Key(pickupState);
            return key.Length > 0 && key == AddressRegions.Key(deliveryState)
                ? ShippingZone.SameProvince
                : ShippingZone.CrossArea;
        }
        if (pickup == delivery)
            return ShippingZone.SameProvince;
        return pickup.Area == delivery.Area ? ShippingZone.SameArea : ShippingZone.CrossArea;
    }
}
