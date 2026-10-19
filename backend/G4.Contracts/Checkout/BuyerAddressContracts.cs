namespace G4.Contracts.Checkout;
public sealed record BuyerAddressInput(string? FullName, string? Street, string? City, string? State, string? Country, bool IsDefault = false);
public sealed record BuyerAddressView(
    int Id,
    string FullName,
    string? Phone,
    string Street,
    string City,
    string State,
    string Country,
    bool IsDefault
);
public sealed record BuyerAddressBook(int? AffectedId, IReadOnlyList<BuyerAddressView> Addresses);

public static class BuyerAddressRules
{
    public const int MaxAddresses = 10;
    private static readonly (string Field, string Label, int Max)[] Fields =
    [
        ("fullName", "tên người nhận", 100),
        ("street", "đường/số nhà", 100),
        ("city", "thành phố", 50),
        ("state", "tỉnh/vùng", 50),
        ("country", "quốc gia", 50)
    ];
    public static string Clean(string? value) => string.Join(' ', (value ?? "").Split((char[]? )null, StringSplitOptions.RemoveEmptyEntries));

    public static BuyerAddressInput Normalize(BuyerAddressInput input) => input with
    {
        FullName = Clean(input.FullName),
        Street = Clean(input.Street),
        City = Clean(input.City),
        State = Clean(input.State),
        Country = Clean(input.Country)
    };
    /// <summary>Returns field name -> Vietnamese error message. Empty when valid.</summary>
    public static Dictionary<string, string> Validate(BuyerAddressInput input)
    {
        var n = Normalize(input);
        var values = new[] { n.FullName!, n.Street!, n.City!, n.State!, n.Country! };
        var errors = new Dictionary<string, string>();
        for (var i = 0; i < Fields.Length; i++)
        {
            var (field, label, max) = Fields[i];
            if (values[i].Length == 0)
                errors[field] = $"Vui lòng nhập {label}.";
            else if (values[i].Length > max)
                errors[field] = $"Tối đa {max} ký tự cho {label}.";
        }

        return errors;
    }
}
