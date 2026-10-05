namespace G4.Domain.Entities;

public partial class OrderItem
{
    public decimal SellerDiscountSnapshot { get; set; }
    public decimal PlatformDiscountSnapshot { get; set; }
    public string? ProductTitleSnapshot { get; set; }
    public int? SellerIdSnapshot { get; set; }
    public decimal? UnitWeightKgSnapshot { get; set; }
}
