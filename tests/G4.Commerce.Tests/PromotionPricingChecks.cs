using G4.Domain.Rules;

internal static class PromotionPricingChecks
{
    public static void Run()
    {
        var checks = new (string Name, Action Check)[]
        {
            ("platform coupon preserves seller gross", PlatformFunding),
            ("fixed sale applies per unit and is capped", FixedSale),
            ("volume tiers use each SKU quantity", VolumeTiers),
            ("order conditions use original eligible amounts", OrderConditions),
            ("mixed sale and volume beats whole-basket alternatives", MixedBasket),
            ("order can beat volume without stacking with it", BestOrder),
            ("coupon excludes volume and order", CouponStacking),
            ("product and exact category scope", Scope),
            ("best shipping discount is capped and eligible", Shipping),
            ("allocation preserves cents with stable remainder ties", Allocation),
            ("percent rounding uses midpoint away from zero", Rounding),
            ("ties minimize distinct programs then IDs", Ties),
            ("saturated coupon does not add unnecessary sale", SaturatedCoupon),
            ("coupon failures explain missing conditions", CouponValidation),
            ("invalid carts cannot produce money", InvalidCart)
        };
        var failures = new List<string>();
        foreach (var (name, check) in checks)
        {
            try { check(); }
            catch (Exception ex) { failures.Add($"{name}: {ex.Message}"); }
        }
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
        Console.WriteLine($"Promotion pricing: {checks.Length} stacking, allocation, eligibility and funding checks passed");
    }

    private static void PlatformFunding()
    {
        var result = Price([Line(1, 100)], [Rule(1, "Sale", 10, true), Rule(2, "Coupon", 5, code: "SAVE", funding: "Platform")], 5, " save ");
        Equal(100m, result.Subtotal); Equal(15m, result.GoodsDiscount); Equal(10m, result.SellerGoodsDiscount);
        Equal(5m, result.PlatformSubsidy); Equal(90m, result.Total); Equal(95m, result.SellerGross);
        Equal(10m, result.Lines.Single().SellerDiscount); Equal(5m, result.Lines.Single().PlatformDiscount);
        Equal(10m, result.Promotions.Single(p => p.Id == 1).Lines.Single().SellerDiscount);
        Equal(5m, result.Promotions.Single(p => p.Id == 2).Lines.Single().PlatformDiscount);
    }
    private static void FixedSale()
    {
        Equal(24m, Price([Line(1, 10, 3)], [Rule(1, "Sale", 2)]).Total);
        var capped = Price([Line(1, .03m, 2)], [Rule(1, "Sale", 1)]);
        Equal(.06m, capped.GoodsDiscount); Equal(0m, capped.Total);
    }
    private static void VolumeTiers()
    {
        var rule = Rule(1, "Volume") with { Tiers = [new(3, 10), new(5, 20)] };
        var result = Price([Line(1, 10, 2), Line(2, 10, 3), Line(3, 10, 5)], [rule]);
        Equal(13m, result.GoodsDiscount); Equal(0m, result.Lines.Single(l => l.ProductId == 1).SellerDiscount);
        Equal(10m, result.Weight);
    }
    private static void OrderConditions()
    {
        var order = Rule(2, "Order", 10, true, products: [1]) with { MinSubtotal = 100, MinQuantity = 2 };
        var result = Price([Line(1, 50, 2), Line(2, 50)], [Rule(1, "Sale", 50, true, products: [1]), order]);
        Equal(95m, result.Total); Equal(5m, result.Promotions.Single(p => p.Id == 2).Amount);
        Equal(100m, Price([Line(1, 50), Line(2, 50)], [order]).Total);
    }
    private static void MixedBasket()
    {
        var result = Price([Line(1, 100), Line(2, 100)], [Rule(1, "Sale", 10, true), Rule(2, "Volume", products: [1]) with { Tiers = [new(1, 50)] }, Rule(3, "Order", 25)]);
        Equal(140m, result.Total); Ids(result, 1, 2);
        Equal(50m, result.Lines.Single(l => l.ProductId == 1).SellerDiscount);
        Equal(10m, result.Lines.Single(l => l.ProductId == 2).SellerDiscount);
    }
    private static void BestOrder()
    {
        var result = Price([Line(1, 100), Line(2, 100)], [Rule(1, "Sale", 10, true), Rule(2, "Volume") with { Tiers = [new(1, 30)] }, Rule(3, "Order", 70)]);
        Equal(110m, result.Total); Ids(result, 1, 3);
    }
    private static void CouponStacking()
    {
        var result = Price([Line(1, 100)], [Rule(1, "Sale", 10, true), Rule(2, "Volume") with { Tiers = [new(1, 90)] }, Rule(3, "Order", 90), Rule(4, "Coupon", 10, code: "SAVE"), Rule(5, "Shipping", free: true)], 5, "SAVE");
        Equal(80m, result.Total); Equal(0m, result.Shipping); Ids(result, 1, 4, 5);
    }
    private static void Scope()
    {
        var sale = Rule(1, "Sale", 10, true, products: [1]) with { CategoryIds = [2] };
        var result = Price([Line(1, 100, category: 8), Line(2, 100, category: 2), Line(3, 100, category: 3)], [sale]);
        Equal(20m, result.GoodsDiscount); Equal(0m, result.Lines.Single(l => l.ProductId == 3).SellerDiscount);
    }
    private static void Shipping()
    {
        var result = Price([Line(1, 20)], [Rule(1, "Shipping", 50, true), Rule(2, "Shipping", 99), Rule(3, "Shipping", free: true) with { MinSubtotal = 21 }], 5.20m);
        Equal(5.20m, result.ShippingDiscount); Equal(20m, result.Total); Ids(result, 2);
        Equal(0m, result.GoodsDiscount); Equal(0m, result.SellerGoodsDiscount);
    }
    private static void Allocation()
    {
        var result = Price([Line(3, .01m), Line(1, .01m), Line(2, .01m)], [Rule(1, "Coupon", .02m, code: "CENT", funding: "Platform")], coupon: "CENT");
        Equal(.01m, result.Total); Equal(.02m, result.PlatformSubsidy);
        Equal(.01m, result.Lines.Single(l => l.ProductId == 1).PlatformDiscount);
        Equal(.01m, result.Lines.Single(l => l.ProductId == 2).PlatformDiscount);
        Equal(0m, result.Lines.Single(l => l.ProductId == 3).PlatformDiscount);
        Equal(.02m, result.Promotions.Single().Lines.Sum(l => l.PlatformDiscount));
        var capped = Price([Line(1, 10), Line(2, 20)], [Rule(2, "Order", 50, true) with { Cap = 1m }]);
        Equal(.33m, capped.Lines.Single(l => l.ProductId == 1).SellerDiscount);
        Equal(.67m, capped.Lines.Single(l => l.ProductId == 2).SellerDiscount);
    }
    private static void Rounding() => Equal(.01m, Price([Line(1, .05m)], [Rule(1, "Sale", 10, true)]).GoodsDiscount);
    private static void Ties()
    {
        var result = Price([Line(1, 10), Line(2, 10)], [Rule(1, "Sale", 1, products: [1]), Rule(2, "Sale", 1, products: [2]), Rule(10, "Sale", 1)]);
        Ids(result, 10);
        Ids(Price([Line(1, 10)], [Rule(9, "Sale", 1), Rule(3, "Sale", 1)]), 3);
        Ids(Price([Line(1, 100)], [Rule(2, "Sale", 100, true), Rule(1, "Order", 100)]), 1);
    }
    private static void SaturatedCoupon()
    {
        var result = Price([Line(1, 10)], [Rule(1, "Sale", 50, true), Rule(2, "Coupon", 100, true, code: "FREE")], coupon: "FREE");
        Equal(0m, result.Total); Ids(result, 2);
    }
    private static void CouponValidation()
    {
        RejectCoupon([], "LOST", "tồn tại");
        RejectCoupon([Rule(1, "Coupon", 1, code: "SAVE", products: [2])], "SAVE", "phạm vi");
        RejectCoupon([Rule(1, "Coupon", 1, code: "SAVE") with { MinSubtotal = 20 }], "SAVE", "giá trị");
        RejectCoupon([Rule(1, "Coupon", 1, code: "SAVE") with { MinQuantity = 2 }], "SAVE", "số lượng");
        Equal(10m, Price([Line(1, 10)], [], coupon: "  ").Total);
    }
    private static void InvalidCart()
    {
        Reject(() => Price([], [])); Reject(() => Price([Line(1, 10, 0)], []));
        Reject(() => Price([Line(1, -1)], [])); Reject(() => Price([Line(1, 10) with { WeightKg = 0 }], []));
        Reject(() => Price([Line(1, 10)], [], -1));
    }
    private static void RejectCoupon(PromotionRule[] rules, string code, string reason)
    {
        try { Price([Line(1, 10)], rules, coupon: code); }
        catch (ArgumentException ex) { if (ex.Message.Contains(reason, StringComparison.OrdinalIgnoreCase)) return; throw; }
        throw new Exception($"Coupon {code} should fail with a Vietnamese {reason} explanation");
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("Invalid pricing input was accepted");
    }
    private static PromotionPriceLine Line(int id, decimal price, int qty = 1, int? category = null) => new(id, category, price, qty, 1m);
    private static PromotionRule Rule(int id, string type, decimal value = 0, bool percent = false, int[]? products = null, string? code = null, string funding = "Seller", bool free = false) =>
        new(id, $"Program {id}", type, funding, funding == "Seller" ? 1 : null, code, value, percent, free, null, 0, 0, 1, products ?? [], [], []);
    private static PromotionPriceResult Price(PromotionPriceLine[] lines, PromotionRule[] rules, decimal shipping = 0, string? coupon = null) => PromotionPricing.Calculate(lines, rules, shipping, coupon);
    private static void Ids(PromotionPriceResult result, params int[] ids) => Equal(string.Join(",", ids), string.Join(",", result.Promotions.Select(p => p.Id)));
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}");
    }
}
