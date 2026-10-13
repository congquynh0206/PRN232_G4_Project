namespace G4.Domain.Entities;

public sealed class BuyerCart
{
    public int BuyerId { get; set; }
    public string ItemsJson { get; set; } = "[]";
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class CheckoutBatch
{
    public string Key { get; set; } = "";
    public int BuyerId { get; set; }
    public string RequestJson { get; set; } = "";
    public string OrderIdsJson { get; set; } = "[]";
}
