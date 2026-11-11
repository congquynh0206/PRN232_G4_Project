using System.Text.RegularExpressions;

namespace G4.Domain.Rules;

/// <summary>Address after validation: canonical country/state, E.164 phone, trimmed text.</summary>
public sealed record DeliveryAddress(
    string FullName,
    string? Phone,
    string Street,
    string City,
    string State,
    string Country,
    string? PostalCode
)
{
    /// <summary>Snapshot text; phone and ZIP are stored in their own columns.</summary>
    public string Text => string.Join(", ", FullName, Street, City, State, Country);
}

/// <summary>
/// Validation shared by the buyer address book, address changes and the seller pickup address.
/// Only Vietnam and the United States are supported. The country decides the dial code:
/// Vietnam +84 (9 digits after the code), United States +1 (10-digit NANP number).
/// </summary>
public static class DeliveryAddressRules
{
    public const int NameMax = 100;
    public const int StreetMax = 100;
    public const int CityMax = 50;
    public const int PostalMax = 20;

    private static readonly Regex UsZip = new(@"\A\d{5}(-\d{4})?\z", RegexOptions.CultureInvariant);
    private static readonly Regex VnPostal = new(@"\A\d{5,6}\z", RegexOptions.CultureInvariant);
    private static readonly Regex VnNumber = new(
        @"\A(?:[35789]\d{8}|2\d{9})\z",
        RegexOptions.CultureInvariant
    );
    private static readonly Regex UsNumber = new(
        @"\A[2-9]\d{2}[2-9]\d{6}\z",
        RegexOptions.CultureInvariant
    );

    public static string Clean(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static string DialCode(string country) =>
        country == AddressRegions.UnitedStates ? "+1" : "+84";

    /// <summary>Returns the E.164 number, or null when it is not valid for the country.</summary>
    public static string? Phone(string? country, string? value)
    {
        var canonical = AddressRegions.Country(country);
        if (canonical is null || string.IsNullOrWhiteSpace(value))
            return null;
        var text = value.Trim();
        if (Regex.IsMatch(text, @"[^\d\s().+-]"))
            return null;
        var digits = new string(text.Where(char.IsDigit).ToArray());
        var code = canonical == AddressRegions.UnitedStates ? "1" : "84";
        if (text.StartsWith('+'))
        {
            if (!digits.StartsWith(code, StringComparison.Ordinal))
                return null;
            digits = digits[code.Length..];
        }
        else if (canonical == AddressRegions.Vietnam && digits.StartsWith('0'))
            digits = digits[1..];
        else if (
            canonical == AddressRegions.UnitedStates
            && digits.Length == 11
            && digits[0] == '1'
        )
            digits = digits[1..];
        var valid = canonical == AddressRegions.UnitedStates
            ? UsNumber.IsMatch(digits)
            : VnNumber.IsMatch(digits);
        return valid ? "+" + code + digits : null;
    }

    /// <summary>
    /// Validates every field and returns field name → Vietnamese message. When there are no
    /// errors, <paramref name="address"/> holds the canonical values to store.
    /// </summary>
    public static Dictionary<string, string> Validate(
        string? fullName,
        string? phone,
        string? street,
        string? city,
        string? state,
        string? country,
        string? postalCode,
        bool phoneRequired,
        out DeliveryAddress? address
    )
    {
        var errors = new Dictionary<string, string>();
        var name = Clean(fullName);
        var line = Clean(street);
        var town = Clean(city);
        var zip = Clean(postalCode);
        Text(errors, "fullName", name, "tên người nhận", NameMax);
        Text(errors, "street", line, "đường/số nhà", StreetMax);
        Text(errors, "city", town, "phường/xã hoặc thành phố", CityMax);

        var canonicalCountry = AddressRegions.Country(Clean(country));
        if (canonicalCountry is null)
            errors["country"] = "Chỉ hỗ trợ địa chỉ tại Việt Nam hoặc Hoa Kỳ.";
        var canonicalState = canonicalCountry is null
            ? null
            : AddressRegions.State(canonicalCountry, Clean(state));
        if (canonicalCountry is not null && canonicalState is null)
            errors["state"] =
                canonicalCountry == AddressRegions.Vietnam
                    ? "Chọn tỉnh/thành phố trong danh sách 34 tỉnh/thành."
                    : "Chọn bang trong danh sách của Hoa Kỳ.";

        if (zip.Length > PostalMax)
            errors["postalCode"] = $"Mã bưu chính tối đa {PostalMax} ký tự.";
        else if (canonicalCountry == AddressRegions.UnitedStates && !UsZip.IsMatch(zip))
            errors["postalCode"] = "ZIP Hoa Kỳ cần 5 chữ số hoặc ZIP+4.";
        else if (
            canonicalCountry == AddressRegions.Vietnam
            && zip.Length > 0
            && !VnPostal.IsMatch(zip)
        )
            errors["postalCode"] = "Mã bưu chính Việt Nam gồm 5 hoặc 6 chữ số.";

        string? e164 = null;
        if (string.IsNullOrWhiteSpace(phone))
        {
            if (phoneRequired)
                errors["phone"] = "Vui lòng nhập số điện thoại người nhận.";
        }
        else if (canonicalCountry is not null)
        {
            e164 = Phone(canonicalCountry, phone);
            if (e164 is null)
                errors["phone"] =
                    canonicalCountry == AddressRegions.Vietnam
                        ? "Số Việt Nam cần 9 số sau +84, ví dụ 0912 345 678."
                        : "Số Hoa Kỳ cần 10 số, ví dụ (212) 555-0123.";
        }

        address = errors.Count == 0
            ? new DeliveryAddress(
                name,
                e164,
                line,
                town,
                canonicalState!,
                canonicalCountry!,
                zip.Length == 0 ? null : zip
            )
            : null;
        return errors;
    }

    private static void Text(
        Dictionary<string, string> errors,
        string field,
        string value,
        string label,
        int max
    )
    {
        if (value.Length == 0)
            errors[field] = $"Vui lòng nhập {label}.";
        else if (value.Length > max)
            errors[field] = $"Tối đa {max} ký tự cho {label}.";
    }
}
