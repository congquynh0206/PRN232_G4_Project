namespace G4.Domain.Rules;

public sealed record FeeBreakdown(decimal VariableFee, decimal FixedFee, decimal TotalFee, decimal NetAmount);

public static class SellerFinanceRules
{
    public static FeeBreakdown CalculateFees(decimal grossAmount, decimal feePercent, decimal fixedFee)
    {
        if (grossAmount <= 0) throw new ArgumentOutOfRangeException(nameof(grossAmount));
        if (feePercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(feePercent));
        if (fixedFee < 0) throw new ArgumentOutOfRangeException(nameof(fixedFee));

        var variableFee = decimal.Round(grossAmount * feePercent / 100m, 2, MidpointRounding.AwayFromZero);
        var appliedFixedFee = Math.Min(fixedFee, grossAmount - variableFee);
        var totalFee = decimal.Round(variableFee + appliedFixedFee, 2, MidpointRounding.AwayFromZero);
        return new FeeBreakdown(variableFee, appliedFixedFee, totalFee,
            decimal.Round(grossAmount - totalFee, 2, MidpointRounding.AwayFromZero));
    }
}
