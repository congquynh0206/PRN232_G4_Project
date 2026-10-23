namespace G4.Domain.Rules;

public sealed record ReturnPrice(
    decimal GoodsPaid,
    decimal OutboundShippingRetained,
    decimal ReturnShippingFee,
    decimal BuyerReturnShippingFee,
    decimal BuyerRefund,
    decimal PlatformSubsidyReversal,
    decimal SellerGrossRefund,
    string ShippingPayer,
    bool CanRequest
);

public static class ReturnPricing
{
    public static string ReasonLabel(string code) =>
        code switch
        {
            "WrongItem" => "Sai hàng",
            "NotAsDescribed" => "Sai mô tả",
            "DamagedOrMissing" => "Hàng hỏng hoặc thiếu do người bán",
            "ChangedMind" => "Đổi ý",
            "OtherBuyerReason" => "Lý do khác không do người bán",
            _ => throw new ArgumentException("Chọn lý do trả hàng hợp lệ."),
        };

    public static ReturnPrice Calculate(
        string reasonCode,
        decimal paid,
        decimal shippingPaid,
        decimal alreadyRefundedShipping,
        decimal platformSubsidy,
        decimal returnShippingFee
    )
    {
        _ = ReasonLabel(reasonCode);
        if (
            paid < 0
            || shippingPaid < 0
            || shippingPaid > paid
            || alreadyRefundedShipping < 0
            || alreadyRefundedShipping > shippingPaid
            || platformSubsidy < 0
            || returnShippingFee <= 0
        )
            throw new InvalidOperationException("Snapshot tiền hoặc phí vận chuyển không hợp lệ.");
        var buyerPays = reasonCode is "ChangedMind" or "OtherBuyerReason";
        var goods = paid - shippingPaid;
        var retained = buyerPays ? shippingPaid - alreadyRefundedShipping : 0m;
        var buyerReturnFee = buyerPays ? returnShippingFee : 0m;
        var available = buyerPays ? goods : paid - alreadyRefundedShipping;
        var allowed = buyerReturnFee <= available;
        var refund = allowed ? available - buyerReturnFee : 0m;
        return new(
            goods,
            retained,
            returnShippingFee,
            buyerReturnFee,
            refund,
            platformSubsidy,
            available + platformSubsidy,
            buyerPays ? "Buyer" : "Seller",
            allowed
        );
    }
}
