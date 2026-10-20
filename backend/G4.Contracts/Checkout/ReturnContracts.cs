namespace G4.Contracts.Checkout;

public sealed record ReturnRequestInput(string ReasonCode, string? Description, string Key, string PricingFingerprint);
public sealed record ReturnQuote(int OrderId, string ReasonCode, string ReasonLabel, string ShippingPayer,
    decimal BuyerPaid, decimal AlreadyRefunded, decimal GoodsPaid, decimal OutboundShippingRetained,
    decimal ReturnShippingFee, decimal BuyerReturnShippingFee, decimal BuyerRefund,
    decimal PlatformSubsidyReversal, decimal SellerGrossRefund, decimal WeightKg,
    string PickupAddress, string DeliveryAddress, string Currency, bool CanRequest, string? BlockReason,
    string PricingFingerprint, DateTime? ReturnWindowClosesAt = null, int ReturnWindowPolicyVersion = 0);
