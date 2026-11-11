using System.Globalization;
using System.Text;

namespace G4.Domain.Rules;

/// <summary>Three shipping areas of Vietnam used by the zone price table.</summary>
public enum VietnamArea
{
    North,
    Central,
    South,
}

public sealed record VietnamProvince(string Name, VietnamArea Area);

public sealed record UsState(string Code, string Name);

/// <summary>
/// Supported delivery countries (Vietnam, United States) and their first-level regions.
/// Vietnam uses the 34 provinces in effect from 1 July 2025; old province names are accepted
/// as aliases of the merged province. US states are stored by their two-letter code.
/// </summary>
public static class AddressRegions
{
    public const string Vietnam = "Vietnam";
    public const string UnitedStates = "United States";

    public static readonly IReadOnlyList<VietnamProvince> VietnamProvinces =
    [
        new("Hà Nội", VietnamArea.North),
        new("Hải Phòng", VietnamArea.North),
        new("Quảng Ninh", VietnamArea.North),
        new("Cao Bằng", VietnamArea.North),
        new("Lạng Sơn", VietnamArea.North),
        new("Lai Châu", VietnamArea.North),
        new("Điện Biên", VietnamArea.North),
        new("Sơn La", VietnamArea.North),
        new("Tuyên Quang", VietnamArea.North),
        new("Lào Cai", VietnamArea.North),
        new("Thái Nguyên", VietnamArea.North),
        new("Phú Thọ", VietnamArea.North),
        new("Bắc Ninh", VietnamArea.North),
        new("Hưng Yên", VietnamArea.North),
        new("Ninh Bình", VietnamArea.North),
        new("Thanh Hóa", VietnamArea.Central),
        new("Nghệ An", VietnamArea.Central),
        new("Hà Tĩnh", VietnamArea.Central),
        new("Quảng Trị", VietnamArea.Central),
        new("Huế", VietnamArea.Central),
        new("Đà Nẵng", VietnamArea.Central),
        new("Quảng Ngãi", VietnamArea.Central),
        new("Gia Lai", VietnamArea.Central),
        new("Đắk Lắk", VietnamArea.Central),
        new("Khánh Hòa", VietnamArea.Central),
        new("Lâm Đồng", VietnamArea.Central),
        new("Hồ Chí Minh", VietnamArea.South),
        new("Đồng Nai", VietnamArea.South),
        new("Tây Ninh", VietnamArea.South),
        new("Cần Thơ", VietnamArea.South),
        new("Vĩnh Long", VietnamArea.South),
        new("Đồng Tháp", VietnamArea.South),
        new("Cà Mau", VietnamArea.South),
        new("An Giang", VietnamArea.South),
    ];

    public static readonly IReadOnlyList<UsState> UsStates =
    [
        new("AL", "Alabama"),
        new("AK", "Alaska"),
        new("AZ", "Arizona"),
        new("AR", "Arkansas"),
        new("CA", "California"),
        new("CO", "Colorado"),
        new("CT", "Connecticut"),
        new("DE", "Delaware"),
        new("DC", "District of Columbia"),
        new("FL", "Florida"),
        new("GA", "Georgia"),
        new("HI", "Hawaii"),
        new("ID", "Idaho"),
        new("IL", "Illinois"),
        new("IN", "Indiana"),
        new("IA", "Iowa"),
        new("KS", "Kansas"),
        new("KY", "Kentucky"),
        new("LA", "Louisiana"),
        new("ME", "Maine"),
        new("MD", "Maryland"),
        new("MA", "Massachusetts"),
        new("MI", "Michigan"),
        new("MN", "Minnesota"),
        new("MS", "Mississippi"),
        new("MO", "Missouri"),
        new("MT", "Montana"),
        new("NE", "Nebraska"),
        new("NV", "Nevada"),
        new("NH", "New Hampshire"),
        new("NJ", "New Jersey"),
        new("NM", "New Mexico"),
        new("NY", "New York"),
        new("NC", "North Carolina"),
        new("ND", "North Dakota"),
        new("OH", "Ohio"),
        new("OK", "Oklahoma"),
        new("OR", "Oregon"),
        new("PA", "Pennsylvania"),
        new("RI", "Rhode Island"),
        new("SC", "South Carolina"),
        new("SD", "South Dakota"),
        new("TN", "Tennessee"),
        new("TX", "Texas"),
        new("UT", "Utah"),
        new("VT", "Vermont"),
        new("VA", "Virginia"),
        new("WA", "Washington"),
        new("WV", "West Virginia"),
        new("WI", "Wisconsin"),
        new("WY", "Wyoming"),
    ];

    // Province names before the 1 July 2025 merger, mapped to the province that absorbed them.
    private static readonly (string Old, string Current)[] MergedProvinces =
    [
        ("Hà Giang", "Tuyên Quang"),
        ("Yên Bái", "Lào Cai"),
        ("Bắc Kạn", "Thái Nguyên"),
        ("Vĩnh Phúc", "Phú Thọ"),
        ("Hòa Bình", "Phú Thọ"),
        ("Bắc Giang", "Bắc Ninh"),
        ("Thái Bình", "Hưng Yên"),
        ("Hải Dương", "Hải Phòng"),
        ("Hà Nam", "Ninh Bình"),
        ("Nam Định", "Ninh Bình"),
        ("Quảng Bình", "Quảng Trị"),
        ("Quảng Nam", "Đà Nẵng"),
        ("Kon Tum", "Quảng Ngãi"),
        ("Bình Định", "Gia Lai"),
        ("Ninh Thuận", "Khánh Hòa"),
        ("Đắk Nông", "Lâm Đồng"),
        ("Bình Thuận", "Lâm Đồng"),
        ("Phú Yên", "Đắk Lắk"),
        ("Bình Dương", "Hồ Chí Minh"),
        ("Bà Rịa - Vũng Tàu", "Hồ Chí Minh"),
        ("Bình Phước", "Đồng Nai"),
        ("Long An", "Tây Ninh"),
        ("Sóc Trăng", "Cần Thơ"),
        ("Hậu Giang", "Cần Thơ"),
        ("Bến Tre", "Vĩnh Long"),
        ("Trà Vinh", "Vĩnh Long"),
        ("Tiền Giang", "Đồng Tháp"),
        ("Bạc Liêu", "Cà Mau"),
        ("Kiên Giang", "An Giang"),
        ("Thừa Thiên Huế", "Huế"),
        ("HCM", "Hồ Chí Minh"),
        ("Ho Chi Minh City", "Hồ Chí Minh"),
        ("Sài Gòn", "Hồ Chí Minh"),
    ];

    private static readonly Dictionary<string, string> CountryKeys = new()
    {
        ["vietnam"] = Vietnam,
        ["vn"] = Vietnam,
        ["vnm"] = Vietnam,
        ["us"] = UnitedStates,
        ["usa"] = UnitedStates,
        ["unitedstates"] = UnitedStates,
        ["unitedstatesofamerica"] = UnitedStates,
        ["hoaky"] = UnitedStates,
        ["my"] = UnitedStates,
    };

    private static readonly Dictionary<string, VietnamProvince> ProvinceKeys = BuildProvinceKeys();

    private static readonly Dictionary<string, UsState> StateKeys = UsStates
        .SelectMany(s => new[] { (Key(s.Code), s), (Key(s.Name), s), ("us" + Key(s.Code), s) })
        .ToDictionary(x => x.Item1, x => x.s);

    /// <summary>"Vietnam" or "United States"; null when the country is unsupported.</summary>
    public static string? Country(string? value) =>
        CountryKeys.TryGetValue(Key(value), out var country) ? country : null;

    /// <summary>
    /// Returns the stored form of a first-level region: the province name for Vietnam, the
    /// two-letter code for the United States. Null when the region is not in the list.
    /// </summary>
    public static string? State(string? country, string? value) =>
        Country(country) switch
        {
            Vietnam => Province(value)?.Name,
            UnitedStates => StateKeys.TryGetValue(Key(value), out var s) ? s.Code : null,
            _ => null,
        };

    public static VietnamProvince? Province(string? value) =>
        ProvinceKeys.TryGetValue(ProvinceKey(value), out var p) ? p : null;

    /// <summary>Lower-case letters and digits only, diacritics removed (đ becomes d).</summary>
    public static string Key(string? value) =>
        new(
            (value ?? "")
                .Replace('đ', 'd')
                .Replace('Đ', 'D')
                .Normalize(NormalizationForm.FormD)
                .Where(c =>
                    CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark
                    && char.IsLetterOrDigit(c)
                )
                .Select(char.ToLowerInvariant)
                .ToArray()
        );

    // "Thành phố Hà Nội", "TP. Hồ Chí Minh", "Tỉnh Nghệ An" match the bare province name.
    private static string ProvinceKey(string? value)
    {
        var key = Key(value);
        foreach (var prefix in new[] { "thanhpho", "tinh", "tp" })
            if (key.Length > prefix.Length && key.StartsWith(prefix, StringComparison.Ordinal))
                return key[prefix.Length..];
        return key;
    }

    private static Dictionary<string, VietnamProvince> BuildProvinceKeys()
    {
        var keys = VietnamProvinces.ToDictionary(p => Key(p.Name));
        foreach (var (old, current) in MergedProvinces)
            keys[Key(old)] = keys[Key(current)];
        return keys;
    }
}
