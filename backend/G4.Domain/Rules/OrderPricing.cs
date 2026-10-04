namespace G4.Domain.Rules;

public readonly record struct PriceLine(decimal UnitPrice, int Quantity, decimal UnitWeightKg = 1m);
public readonly record struct PriceQuote(decimal Subtotal, decimal Discount, decimal Shipping, decimal Total, decimal TotalWeightKg = 0m);

public static class OrderPricing
{
    public static PriceQuote Calculate(IEnumerable<PriceLine> lines, decimal couponPercent, bool sameState)
    {
        if (couponPercent is < 0 or > 20) throw new ArgumentOutOfRangeException(nameof(couponPercent));
        var materialized = lines.ToArray();
        if (materialized.Length == 0) throw new ArgumentException("Cart is empty", nameof(lines));
        if (materialized.Any(line => line.Quantity <= 0 || line.UnitPrice < 0 || line.UnitWeightKg <= 0))
            throw new ArgumentOutOfRangeException(nameof(lines));
        var subtotal = materialized.Sum(line => line.UnitPrice * line.Quantity);
        var discount = Math.Round(subtotal * couponPercent / 100m, 2, MidpointRounding.AwayFromZero);
        var kilograms = materialized.Sum(line => line.UnitWeightKg * line.Quantity);
        var shipping = ShippingPricing.Calculate(kilograms, sameState);
        return new PriceQuote(subtotal, discount, shipping, subtotal - discount + shipping, kilograms);
    }
}
