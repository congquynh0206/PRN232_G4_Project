namespace G4.Domain.Entities;
public partial class OrderTable
{
    public int PricingSchemaVersion { get; set; }
    public decimal ShippingBase { get; set; }
    public decimal ShippingDiscount { get; set; }
    public decimal SellerGoodsDiscount { get; set; }
    public decimal PlatformSubsidy { get; set; }
    public decimal SellerGrossSnapshot { get; set; }
    public string? PricingFingerprint { get; set; }
}
