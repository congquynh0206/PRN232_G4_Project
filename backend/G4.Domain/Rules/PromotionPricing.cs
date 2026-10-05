namespace G4.Domain.Rules;

public record PromotionPriceLine(int ProductId, int? CategoryId, decimal UnitPrice, int Quantity, decimal WeightKg);
public record PromotionTierRule(int MinQuantity, decimal Percent);
public record PromotionRule(int Id, string Name, string Type, string FundingSource, int? SellerId, string? Code,
    decimal Value, bool IsPercent, bool FreeShipping, decimal? Cap, decimal MinSubtotal, int MinQuantity, int Version,
    IReadOnlyList<int> ProductIds, IReadOnlyList<int> CategoryIds, IReadOnlyList<PromotionTierRule> Tiers);
public record PromotionLinePrice(int ProductId, decimal OriginalTotal, decimal SellerDiscount, decimal PlatformDiscount);
public record AppliedPromotion(int Id, string Name, string Type, string FundingSource, string? Code, int Version,
    decimal Amount, IReadOnlyList<PromotionLinePrice> Lines);
public record PromotionPriceResult(decimal Subtotal, decimal GoodsDiscount, decimal ShippingBase, decimal ShippingDiscount,
    decimal SellerGoodsDiscount, decimal PlatformSubsidy, decimal Total, decimal Weight,
    IReadOnlyList<PromotionLinePrice> Lines, IReadOnlyList<AppliedPromotion> Promotions)
{
    public decimal Shipping => ShippingBase - ShippingDiscount;
    public decimal SellerGross => Total + PlatformSubsidy;
}

public static class PromotionPricing
{
    public static PromotionPriceResult Calculate(IReadOnlyList<PromotionPriceLine> lines, IReadOnlyList<PromotionRule> rules,
        decimal shippingBase, string? couponCode = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(rules);
        if (lines.Count == 0) throw new ArgumentException("Giỏ hàng trống.", nameof(lines));
        if (lines.Any(l => l.Quantity <= 0 || l.UnitPrice < 0 || l.WeightKg <= 0) ||
            lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
            throw new ArgumentException("Sản phẩm, số lượng, giá hoặc khối lượng trong giỏ hàng không hợp lệ.", nameof(lines));
        if (shippingBase < 0) throw new ArgumentOutOfRangeException(nameof(shippingBase));

        var cart = lines.OrderBy(l => l.ProductId).ToArray();
        var originals = cart.Select(l => Money(l.UnitPrice * l.Quantity)).ToArray();
        var subtotal = originals.Sum();
        shippingBase = Money(shippingBase);
        var programs = rules.OrderBy(r => r.Id).ToArray();
        var normalizedCode = couponCode?.Trim().ToUpperInvariant();
        PromotionRule? coupon = null;
        if (!string.IsNullOrEmpty(normalizedCode))
        {
            coupon = programs.FirstOrDefault(r => r.Type == "Coupon" &&
                string.Equals(r.Code?.Trim(), normalizedCode, StringComparison.OrdinalIgnoreCase));
            if (coupon is null) throw new ArgumentException("Mã khuyến mãi không tồn tại hoặc không còn hiệu lực.", nameof(couponCode));
            var eligible = EligibleIndexes(coupon, cart);
            if (eligible.Length == 0) throw new ArgumentException("Mã khuyến mãi không áp dụng cho sản phẩm trong phạm vi giỏ hàng.", nameof(couponCode));
            if (eligible.Sum(i => originals[i]) < coupon.MinSubtotal)
                throw new ArgumentException("Giá trị hàng đủ điều kiện chưa đạt giá trị tối thiểu của mã khuyến mãi.", nameof(couponCode));
            if (eligible.Sum(i => (long)cart[i].Quantity) < coupon.MinQuantity)
                throw new ArgumentException("Số lượng hàng đủ điều kiện chưa đạt số lượng tối thiểu của mã khuyến mãi.", nameof(couponCode));
        }

        var shipping = programs.Where(r => r.Type == "Shipping" && Qualifies(r, cart, originals))
            .Select(r => (Rule: r, Amount: Amount(r, shippingBase, r.FreeShipping ? shippingBase : null)))
            .Where(p => p.Amount > 0).OrderByDescending(p => p.Amount).ThenBy(p => p.Rule.Id).FirstOrDefault();
        var shippingDiscount = shipping.Amount;
        Candidate? best = null;

        // Each search fixes the optional basket discount. This keeps Volume and Order mutually exclusive.
        if (coupon is not null) Search(false, coupon);
        else
        {
            Search(true, null);
            foreach (var order in programs.Where(r => r.Type == "Order" && Qualifies(r, cart, originals)))
                Search(false, order);
        }

        var chosen = best!;
        var applied = new List<AppliedPromotion>();
        foreach (var group in chosen.Choices.Select((c, i) => (Choice: c, Index: i))
                     .Where(x => x.Choice.Rule is not null && x.Choice.Amount > 0).GroupBy(x => x.Choice.Rule!.Id))
        {
            var rule = group.First().Choice.Rule!;
            var allocation = new decimal[cart.Length];
            foreach (var entry in group) allocation[entry.Index] = entry.Choice.Amount;
            applied.Add(Snapshot(rule, allocation.Sum(), allocation));
        }
        if (chosen.BasketRule is not null && (chosen.BasketAmount > 0 || chosen.BasketRule.Type == "Coupon"))
        {
            var eligible = EligibleIndexes(chosen.BasketRule, cart);
            var balances = originals.Select((v, i) => eligible.Contains(i) ? v - chosen.Choices[i].Amount : 0m).ToArray();
            applied.Add(Snapshot(chosen.BasketRule, chosen.BasketAmount, Allocate(chosen.BasketAmount, balances, cart)));
        }
        if (shipping.Rule is not null)
            applied.Add(new(shipping.Rule.Id, shipping.Rule.Name, shipping.Rule.Type, shipping.Rule.FundingSource,
                shipping.Rule.Code, shipping.Rule.Version, shipping.Amount, []));
        applied.Sort((a, b) => a.Id.CompareTo(b.Id));

        var totals = cart.Select((l, i) => new PromotionLinePrice(l.ProductId, originals[i],
            applied.SelectMany(p => p.Lines).Where(p => p.ProductId == l.ProductId).Sum(p => p.SellerDiscount),
            applied.SelectMany(p => p.Lines).Where(p => p.ProductId == l.ProductId).Sum(p => p.PlatformDiscount))).ToArray();
        var sellerDiscount = totals.Sum(l => l.SellerDiscount);
        var platformDiscount = totals.Sum(l => l.PlatformDiscount);
        return new(subtotal, sellerDiscount + platformDiscount, shippingBase, shippingDiscount, sellerDiscount,
            platformDiscount, subtotal - sellerDiscount - platformDiscount + shippingBase - shippingDiscount,
            cart.Sum(l => l.WeightKg * l.Quantity), totals, applied);

        AppliedPromotion Snapshot(PromotionRule rule, decimal amount, decimal[] allocations) =>
            new(rule.Id, rule.Name, rule.Type, rule.FundingSource, rule.Code, rule.Version, amount,
                allocations.Select((value, i) => new PromotionLinePrice(cart[i].ProductId, originals[i],
                    rule.FundingSource == "Platform" ? 0m : value, rule.FundingSource == "Platform" ? value : 0m))
                    .Where(l => l.SellerDiscount > 0 || l.PlatformDiscount > 0).ToArray());

        void Search(bool includeVolume, PromotionRule? basketRule)
        {
            var basketIndexes = basketRule is null ? [] : EligibleIndexes(basketRule, cart);
            var basketMask = Enumerable.Range(0, cart.Length).Select(basketIndexes.Contains).ToArray();
            var options = cart.Select((line, i) => programs.Where(r =>
                    (r.Type == "Sale" || includeVolume && r.Type == "Volume") && Matches(r, line))
                .Select(r => new Choice(r, ProductAmount(r, line, originals[i])))
                .Where(c => c.Amount > 0).Append(new Choice(null, 0m))
                .OrderByDescending(c => c.Amount).ThenBy(c => c.Rule?.Id ?? 0).ToArray()).ToArray();
            var remainingMax = new decimal[cart.Length + 1];
            var remainingEligibleMax = new decimal[cart.Length + 1];
            for (var i = cart.Length - 1; i >= 0; i--)
            {
                remainingMax[i] = remainingMax[i + 1] + options[i][0].Amount;
                remainingEligibleMax[i] = remainingEligibleMax[i + 1] + (basketMask[i] ? options[i][0].Amount : 0m);
            }
            var basketOriginal = basketIndexes.Sum(i => originals[i]);
            var choices = new Choice[cart.Length];
            var used = new Dictionary<int, int>();
            // A saturated basket discount may need no product programs at all. Seed that
            // alternative before searching so ties do not enumerate every unnecessary Sale.
            var bareAmount = basketRule is null ? 0m : Amount(basketRule, basketOriginal);
            Consider(Enumerable.Repeat(new Choice(null, 0m), cart.Length).ToArray(), 0m, bareAmount);
            Visit(0, 0m, 0m);

            void Consider(Choice[] selected, decimal productAmount, decimal basketAmount)
            {
                var ids = selected.Where(c => c.Rule is not null).Select(c => c.Rule!.Id)
                    .Concat(basketAmount > 0 ? [basketRule!.Id] : [])
                    .Concat(shipping.Rule is null ? [] : [shipping.Rule.Id]).Distinct().Order().ToArray();
                var candidate = new Candidate(productAmount + basketAmount, ids, selected, basketRule, basketAmount);
                if (best is null || Better(candidate, best)) best = candidate;
            }

            void Visit(int index, decimal productDiscount, decimal eligibleDiscount)
            {
                var optimisticBasket = basketRule is null ? 0m :
                    Amount(basketRule, basketOriginal - eligibleDiscount - remainingEligibleMax[index]);
                var optimistic = productDiscount + remainingMax[index] + optimisticBasket;
                if (best is not null && optimistic < best.GoodsDiscount) return;
                var minimumPrograms = used.Count + (shipping.Rule is null ? 0 : 1) + (optimisticBasket > 0 ? 1 : 0);
                if (best is not null && optimistic == best.GoodsDiscount && minimumPrograms > best.Ids.Length) return;
                if (index == cart.Length)
                {
                    var basketAmount = basketRule is null ? 0m : Amount(basketRule, basketOriginal - eligibleDiscount);
                    Consider((Choice[])choices.Clone(), productDiscount, basketAmount);
                    return;
                }
                foreach (var option in options[index])
                {
                    choices[index] = option;
                    if (option.Rule is not null) used[option.Rule.Id] = used.GetValueOrDefault(option.Rule.Id) + 1;
                    Visit(index + 1, productDiscount + option.Amount, eligibleDiscount + (basketMask[index] ? option.Amount : 0m));
                    if (option.Rule is not null && --used[option.Rule.Id] == 0) used.Remove(option.Rule.Id);
                }
            }
        }
    }

    private static bool Matches(PromotionRule rule, PromotionPriceLine line) =>
        rule.ProductIds.Count == 0 && rule.CategoryIds.Count == 0 || rule.ProductIds.Contains(line.ProductId) ||
        line.CategoryId is int categoryId && rule.CategoryIds.Contains(categoryId);

    private static int[] EligibleIndexes(PromotionRule rule, PromotionPriceLine[] cart) =>
        Enumerable.Range(0, cart.Length).Where(i => Matches(rule, cart[i])).ToArray();

    private static bool Qualifies(PromotionRule rule, PromotionPriceLine[] cart, decimal[] originals)
    {
        var eligible = EligibleIndexes(rule, cart);
        return eligible.Length > 0 && eligible.Sum(i => originals[i]) >= rule.MinSubtotal &&
            eligible.Sum(i => (long)cart[i].Quantity) >= rule.MinQuantity;
    }

    private static decimal ProductAmount(PromotionRule rule, PromotionPriceLine line, decimal original)
    {
        if (rule.Type == "Sale") return Amount(rule, original, rule.IsPercent ? null : rule.Value * line.Quantity);
        var tier = rule.Tiers.Where(t => t.MinQuantity <= line.Quantity).OrderByDescending(t => t.MinQuantity).FirstOrDefault();
        return tier is null ? 0m : Math.Min(original, Money(original * tier.Percent / 100m));
    }

    private static decimal Amount(PromotionRule rule, decimal eligibleAmount, decimal? fixedOverride = null)
    {
        var amount = fixedOverride ?? (rule.IsPercent ? eligibleAmount * rule.Value / 100m : rule.Value);
        if (rule.Cap is decimal cap) amount = Math.Min(amount, cap);
        return Math.Clamp(Money(amount), 0m, eligibleAmount);
    }

    private static decimal[] Allocate(decimal amount, decimal[] balances, PromotionPriceLine[] cart)
    {
        var total = balances.Sum();
        var allocations = new decimal[balances.Length];
        if (amount == 0 || total == 0) return allocations;
        var fractions = new decimal[balances.Length];
        for (var i = 0; i < balances.Length; i++)
        {
            var exact = amount * balances[i] / total;
            allocations[i] = decimal.Floor(exact * 100m) / 100m;
            fractions[i] = exact - allocations[i];
        }
        var pennies = (int)((amount - allocations.Sum()) * 100m);
        foreach (var i in Enumerable.Range(0, balances.Length).Where(i => allocations[i] < balances[i])
                     .OrderByDescending(i => fractions[i]).ThenBy(i => cart[i].ProductId).Take(pennies))
            allocations[i] += .01m;
        return allocations;
    }

    private static bool Better(Candidate candidate, Candidate previous)
    {
        if (candidate.GoodsDiscount != previous.GoodsDiscount) return candidate.GoodsDiscount > previous.GoodsDiscount;
        if (candidate.Ids.Length != previous.Ids.Length) return candidate.Ids.Length < previous.Ids.Length;
        for (var i = 0; i < candidate.Ids.Length; i++)
            if (candidate.Ids[i] != previous.Ids[i]) return candidate.Ids[i] < previous.Ids[i];
        return false;
    }

    private static decimal Money(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    private sealed record Choice(PromotionRule? Rule, decimal Amount);
    private sealed record Candidate(decimal GoodsDiscount, int[] Ids, Choice[] Choices, PromotionRule? BasketRule, decimal BasketAmount);
}
