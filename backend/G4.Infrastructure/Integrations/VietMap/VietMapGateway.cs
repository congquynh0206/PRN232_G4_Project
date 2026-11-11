using System.Text.Json;
using G4.Infrastructure.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Integrations.VietMap;

/// <summary>
/// VietMap Autocomplete v4 + Place v4. Uses the new two-level administrative units
/// (ward, province) introduced on 1 July 2025. The API key stays on the server; results are
/// cached briefly because the free plan allows about 500 autocomplete calls per day.
/// </summary>
public sealed class VietMapGateway(
    HttpClient http,
    IConfiguration config,
    IMemoryCache cache,
    IIntegrationLogWriter? logs = null
) : IAddressLookupGateway
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    private readonly string apiKey = config["VietMap:ApiKey"] ?? "";
    private readonly string baseUrl = (
        config["VietMap:BaseUrl"] ?? "https://maps.vietmap.vn"
    ).TrimEnd('/');

    public bool Enabled => !string.IsNullOrWhiteSpace(apiKey);

    public async Task<IReadOnlyList<AddressSuggestion>> SuggestAsync(
        string text,
        CancellationToken ct
    )
    {
        var key = "vietmap:suggest:" + text.Trim().ToLowerInvariant();
        if (cache.TryGetValue(key, out IReadOnlyList<AddressSuggestion>? hit) && hit != null)
            return hit;
        var query =
            $"/api/autocomplete/v4?display_type=1&layers=ADDRESS,STREET"
            + $"&text={Uri.EscapeDataString(text.Trim())}";
        using var body = await GetAsync("Autocomplete", query, ct);
        var items = new List<AddressSuggestion>();
        if (body.RootElement.ValueKind == JsonValueKind.Array)
            foreach (var row in body.RootElement.EnumerateArray())
            {
                var reference = Text(row, "ref_id");
                var display = Text(row, "display");
                if (reference.Length == 0 || display.Length == 0)
                    continue;
                items.Add(new(reference, display));
                // A row with ward + province is complete: picking it needs no Place call.
                if (FromBoundaries(row) is { } detail)
                    cache.Set(PlaceKey(reference), detail, CacheFor);
            }
        cache.Set(key, (IReadOnlyList<AddressSuggestion>)items, CacheFor);
        return items;
    }

    private static string PlaceKey(string reference) => "vietmap:place:" + reference;

    // "name" is house number + street ("197 Trần Phú"); boundary type 2 = ward, 0 = province.
    private static AddressSuggestionDetail? FromBoundaries(JsonElement row)
    {
        if (
            !row.TryGetProperty("boundaries", out var list)
            || list.ValueKind != JsonValueKind.Array
        )
            return null;
        string ward = "",
            province = "";
        foreach (var b in list.EnumerateArray())
        {
            var type = b.TryGetProperty("type", out var t) && t.TryGetInt32(out var n) ? n : -1;
            if (type == 2)
                ward = Text(b, "full_name");
            else if (type == 0)
                province = AddressRegions.Province(Text(b, "name"))?.Name ?? "";
        }
        var street = Text(row, "name");
        return street.Length > 0 && ward.Length > 0 && province.Length > 0
            ? Detail(street, ward, province)
            : null;
    }

    private static AddressSuggestionDetail Detail(string street, string ward, string province) =>
        new(
            Limit(street, DeliveryAddressRules.StreetMax),
            Limit(ward, DeliveryAddressRules.CityMax),
            province,
            AddressRegions.Vietnam,
            null
        );

    public async Task<AddressSuggestionDetail?> DetailAsync(
        string reference,
        CancellationToken ct
    )
    {
        var key = PlaceKey(reference);
        if (cache.TryGetValue(key, out AddressSuggestionDetail? hit) && hit != null)
            return hit;
        using var body = await GetAsync(
            "Place",
            "/api/place/v4?refid=" + Uri.EscapeDataString(reference),
            ct
        );
        var root = body.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;
        // Live Place v4 returns "address" = ward + province, so build the street from
        // hs_num + street ("197" + "Trần Phú"), falling back to "name".
        var province = AddressRegions.Province(Text(root, "city"));
        var street = string.Join(
            ' ',
            new[] { Text(root, "hs_num"), Text(root, "street") }.Where(x => x.Length > 0)
        );
        if (Text(root, "street").Length == 0)
            street = Text(root, "name");
        var detail = Detail(street, Text(root, "ward"), province?.Name ?? "");
        cache.Set(key, detail, CacheFor);
        return detail;
    }

    private async Task<JsonDocument> GetAsync(
        string operation,
        string pathAndQuery,
        CancellationToken ct
    )
    {
        if (!Enabled)
            throw new AddressLookupUnavailableException("Chưa cấu hình khóa VietMap.");
        await using var call = new IntegrationCallRecorder(logs).Start(
            "VietMap",
            operation,
            "Live"
        );
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            baseUrl + pathAndQuery + "&apikey=" + Uri.EscapeDataString(apiKey)
        );
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await http.SendAsync(request, timeout.Token);
            call.HttpStatus = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                // 401 = wrong key, 423 = quota used up or API not enabled for the key.
                await call.CompleteAsync(
                    "Failed",
                    errorCode: "VietMap" + (int)response.StatusCode,
                    ct: CancellationToken.None
                );
                throw new AddressLookupUnavailableException(
                    (int)response.StatusCode == 423
                        ? "VietMap đã hết hạn mức hôm nay."
                        : "VietMap từ chối yêu cầu."
                );
            }
            var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(timeout.Token),
                cancellationToken: timeout.Token
            );
            await call.CompleteAsync("Succeeded", ct: CancellationToken.None);
            return document;
        }
        catch (AddressLookupUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
            when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            await call.CompleteAsync(
                "Failed",
                errorCode: ex.GetType().Name,
                ct: CancellationToken.None
            );
            if (ct.IsCancellationRequested)
                throw;
            throw new AddressLookupUnavailableException("Không kết nối được VietMap.");
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? DeliveryAddressRules.Clean(value.GetString())
            : "";

    private static string Limit(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
