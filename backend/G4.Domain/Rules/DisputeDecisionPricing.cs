namespace G4.Domain.Rules;

public sealed record DecisionPrice(decimal MaximumRefund, decimal BuyerRefund, decimal OutboundShippingRetained,
    decimal BuyerShippingRecovery, decimal SubsidyReversal, decimal SellerGrossRefund, decimal FeeCredit,
    decimal SellerNetDebit, string? ShippingPayer);

public static class DisputeDecisionPricing
{
    public static DecisionPrice Calculate(string outcome, string responsibility, decimal? requestedAmount,
        decimal remainingPaid, decimal goodsPaid, decimal remainingSubsidy, decimal postage,
        decimal gross, decimal alreadyGrossRefunded, decimal originalFee, decimal alreadyFeeCredited)
    {
        if (outcome is not ("SellerWins" or "FullRefund" or "PartialRefund") || responsibility is not ("Seller" or "Buyer"))
            throw new ArgumentException("Chọn kết quả và kết luận trách nhiệm hợp lệ.");
        if (remainingPaid < 0 || goodsPaid < 0 || goodsPaid > remainingPaid || remainingSubsidy < 0 || postage < 0
            || gross < 0 || alreadyGrossRefunded < 0 || alreadyGrossRefunded > gross || originalFee < 0
            || originalFee > gross || alreadyFeeCredited < 0 || alreadyFeeCredited > originalFee)
            throw new InvalidOperationException("Snapshot tài chính không hợp lệ.");
        if (outcome == "SellerWins")
        {
            if (responsibility != "Buyer" || requestedAmount != null)
                throw new ArgumentException("Seller thắng phải có kết luận không thuộc trách nhiệm seller và không nhập tiền hoàn.");
            return new(0, 0, 0, 0, 0, 0, 0, 0, postage > 0 ? "Buyer" : null);
        }
        var buyerPays = responsibility == "Buyer";
        var basis = buyerPays ? goodsPaid : remainingPaid;
        var recovery = buyerPays ? postage : 0m;
        if (recovery > basis) throw new InvalidOperationException("Phí trả hàng vượt khoản có thể hoàn theo kết luận này.");
        var maximum = basis - recovery;
        var amount = outcome == "FullRefund" ? maximum : requestedAmount ?? throw new ArgumentException("Nhập khoản buyer thực nhận.");
        if (outcome == "FullRefund" && requestedAmount != null || amount < 0 || amount > maximum || decimal.Round(amount, 2) != amount
            || outcome == "PartialRefund" && (amount <= 0 || amount >= maximum))
            throw new ArgumentException("Khoản hoàn một phần phải lớn hơn 0, nhỏ hơn mức tối đa và có tối đa 2 chữ số thập phân.");
        // Buyer postage is deducted once in full. Funding follows the refunded value
        // before that deduction; a full entitlement reverses all remaining subsidy.
        var subsidy = outcome == "FullRefund" || basis == 0 ? remainingSubsidy
            : Money(remainingSubsidy * (amount + recovery) / basis);
        var grossRefund = amount + recovery + subsidy;
        if (grossRefund > gross - alreadyGrossRefunded) throw new InvalidOperationException("Khoản hoàn vượt Gross seller còn lại.");
        var fee = gross == 0 ? 0 : Math.Min(originalFee - alreadyFeeCredited,
            Math.Max(0, Money(originalFee * (alreadyGrossRefunded + grossRefund) / gross) - alreadyFeeCredited));
        return new(maximum, amount, buyerPays ? remainingPaid - goodsPaid : 0m, recovery, subsidy,
            grossRefund, fee, grossRefund - fee - recovery, postage > 0 ? responsibility : null);
    }
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
