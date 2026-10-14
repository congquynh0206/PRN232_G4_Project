namespace G4.Domain.Entities;
public partial class OrderTable
{
    public int AddressChangeCount { get; set; }
    public string? DeliveryStateSnapshot { get; set; }
    public string? DeliveryCountrySnapshot { get; set; }
    public string? PickupStateSnapshot { get; set; }
    public string? PickupCountrySnapshot { get; set; }
}
