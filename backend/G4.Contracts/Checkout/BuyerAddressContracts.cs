namespace G4.Contracts.Checkout;

public sealed record BuyerAddressInput(
    string? FullName,
    string? Street,
    string? City,
    string? State,
    string? Country,
    bool IsDefault = false,
    string? PostalCode = null,
    string? Phone = null
);

public sealed record BuyerAddressView(
    int Id,
    string FullName,
    string? Phone,
    string Street,
    string City,
    string State,
    string Country,
    bool IsDefault,
    string? PostalCode = null
);

public sealed record BuyerAddressBook(int? AffectedId, IReadOnlyList<BuyerAddressView> Addresses);

/// <summary>Field validation lives in G4.Domain.Rules.DeliveryAddressRules.</summary>
public static class BuyerAddressRules
{
    public const int MaxAddresses = 10;
}
