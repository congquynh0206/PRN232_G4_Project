using System.Globalization;

namespace backend.Checkout;

public static class FakeCardProcessor
{
    public static string Outcome(string number, string expiry)
    {
        if (!DateTime.TryParseExact("01/" + expiry, "dd/MM/yy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var month)) return "Invalid";
        if (month.Year < DateTime.UtcNow.Year || (month.Year == DateTime.UtcNow.Year && month.Month < DateTime.UtcNow.Month))
            return "Expired";
        return number switch
        {
            "4111111111111111" => "Succeeded",
            "4000000000000002" => "Declined",
            _ => "Invalid"
        };
    }
}
