using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using G4.Application.Integrations;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Integrations.Klarna;

// Playground-only Klarna Payments + HPP (PLACE_ORDER) + Order Management gateway.
public sealed class KlarnaGateway(HttpClient http, IConfiguration configuration) : IKlarnaGateway
{
    private static readonly HashSet<string> UsStates = new(
        "AL AK AZ AR CA CO CT DE FL GA HI ID IL IN IA KS KY LA ME MD MA MI MN MS MO MT NE NV NH NJ NM NY NC ND OH OK OR PA RI SC SD TN TX UT VT VA WA WV WI WY DC".Split(' '),
        StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly string baseUrl = ValidateBaseUrl(configuration["Klarna:BaseUrl"]);
    private readonly string username = configuration["Klarna:Username"] ?? "";
    private readonly string password = configuration["Klarna:Password"] ?? "";

    public async Task<KlarnaSession> StartAsync(KlarnaCheckoutRequest request, CancellationToken ct)
    {
        ValidateCheckout(request);
        var address = request.ShippingAddress;
        var fullName = address.FullName.Trim();
        var surnameAt = fullName.LastIndexOf(' ');
        var lines = request.Lines.Select(line => new
        {
            type = line.Type,
            reference = line.Reference,
            name = line.Name,
            quantity = line.Quantity,
            unit_price = line.UnitPriceMinor,
            total_amount = checked(line.UnitPriceMinor * line.Quantity),
            total_tax_amount = 0,
            tax_rate = 0,
        }).ToArray();
        var kp = await SendJsonAsync(HttpMethod.Post, "/payments/v1/sessions", new
        {
            acquiring_channel = "ECOMMERCE",
            intent = "buy",
            purchase_country = "US",
            purchase_currency = "USD",
            locale = "en-US",
            order_amount = request.OrderAmountMinor,
            order_tax_amount = 0,
            order_lines = lines,
            merchant_reference1 = request.MerchantReference,
            shipping_address = new
            {
                given_name = surnameAt < 0 ? fullName : fullName[..surnameAt],
                family_name = surnameAt < 0 ? null : fullName[(surnameAt + 1)..],
                street_address = address.Street,
                city = address.City,
                region = address.Region,
                postal_code = string.IsNullOrWhiteSpace(address.PostalCode) ? null : address.PostalCode,
                country = "US",
                phone = address.Phone,
            },
        }, null, ct);
        var kpId = RequiredString(kp, "session_id");
        if (!kp.TryGetProperty("payment_method_categories", out var categories)
            || categories.ValueKind != JsonValueKind.Array)
            throw new HttpRequestException("Klarna session has no payment categories");
        var bnplCategories = categories.EnumerateArray()
            .Select(x => OptionalString(x, "identifier"))
            .Where(x => x is "pay_later" or "pay_over_time")
            .Distinct().ToArray();
        if (bnplCategories.Length == 0)
            throw new HttpRequestException("Klarna BNPL is not available for this session");
        // HPP requires a KP session URL on the same playground API host.
        var kpUrl = baseUrl + "/payments/v1/sessions/" + Uri.EscapeDataString(kpId);
        var hpp = await SendJsonAsync(HttpMethod.Post, "/hpp/v1/sessions", new
        {
            payment_session_url = kpUrl,
            merchant_urls = new
            {
                success = AppendPlaceholders(request.SuccessUrl, true),
                cancel = AppendPlaceholders(request.CancelUrl, false),
            },
            options = new { place_order_mode = "PLACE_ORDER", payment_method_categories = bnplCategories },
        }, null, ct);
        var sessionUrl = RequiredString(hpp, "session_url");
        ValidateHppUrl(sessionUrl);
        DateTimeOffset? expiresAt = null;
        if (hpp.TryGetProperty("expires_at", out var expiry) && expiry.ValueKind != JsonValueKind.Null)
        {
            if (expiry.ValueKind != JsonValueKind.String || !expiry.TryGetDateTimeOffset(out var parsed))
                throw new HttpRequestException("Invalid Klarna HPP expires_at");
            expiresAt = parsed;
        }
        return new KlarnaSession(kpId, RequiredString(hpp, "session_id"), sessionUrl,
            RequiredString(hpp, "redirect_url"), expiresAt);
    }

    public async Task<KlarnaHppStatus> ReadHppAsync(string sessionUrl, CancellationToken ct)
    {
        var hpp = await SendJsonAsync(HttpMethod.Get, ValidateHppUrl(sessionUrl), null, null, ct);
        var status = RequiredString(hpp, "status");
        if (status is not ("WAITING" or "IN_PROGRESS" or "COMPLETED" or "FAILED" or "CANCELLED" or "BACK" or "ERROR" or "DISABLED"))
            throw new HttpRequestException("Unknown Klarna HPP status");
        var orderId = OptionalString(hpp, "order_id");
        if (status == "COMPLETED" && string.IsNullOrWhiteSpace(orderId))
            throw new HttpRequestException("Completed PLACE_ORDER session has no order ID");
        return new KlarnaHppStatus(status, status == "COMPLETED" ? orderId : null);
    }

    public async Task<KlarnaOrderStatus> ReadOrderAsync(string orderId, CancellationToken ct)
    {
        var path = OrderPath(orderId);
        var order = await SendJsonAsync(HttpMethod.Get, path, null, null, ct);
        var returnedId = RequiredString(order, "order_id");
        if (!string.Equals(returnedId, orderId, StringComparison.Ordinal))
            throw new HttpRequestException("Klarna returned a different order ID");
        var amount = RequiredInt64(order, "order_amount");
        var captured = RequiredInt64(order, "captured_amount");
        var currency = RequiredString(order, "purchase_currency");
        var country = RequiredString(order, "purchase_country");
        var status = RequiredString(order, "status");
        if (status is not ("AUTHORIZED" or "PART_CAPTURED" or "CAPTURED" or "CANCELLED" or "EXPIRED" or "CLOSED"))
            throw new HttpRequestException("Unknown Klarna order status");
        if (amount < 0 || captured < 0 || captured > amount || currency != "USD" || country != "US")
            throw new HttpRequestException("Klarna order amount or currency is invalid");
        // GET order normally embeds captures; older/partial representations can omit them.
        JsonElement captures;
        if (!order.TryGetProperty("captures", out captures) || captures.ValueKind != JsonValueKind.Array
            || (captured > 0 && captures.GetArrayLength() == 0))
            captures = await SendJsonAsync(HttpMethod.Get, path + "/captures", null, null, ct);
        if (captures.ValueKind != JsonValueKind.Array)
            throw new HttpRequestException("Klarna captures response is invalid");
        // shortcut: one capture ID fits the contract; multiple captures need a capture ledger.
        if (captures.GetArrayLength() > 1)
            throw new HttpRequestException("Multiple Klarna captures cannot be represented by one capture ID");
        var captureId = captures.GetArrayLength() == 1
            ? RequiredString(captures[0], "capture_id") : null;
        if (captured > 0 && captureId == null)
            throw new HttpRequestException("Captured Klarna order has no capture ID");
        return new KlarnaOrderStatus(returnedId, status, amount, captured, currency,
            OptionalString(order, "merchant_reference1"), captureId);
    }

    public async Task<KlarnaCapture> CaptureAsync(string orderId, long amountMinor, string key, CancellationToken ct)
    {
        PositiveAmount(amountMinor);
        var response = await SendAsync(HttpMethod.Post, OrderPath(orderId) + "/captures",
            new { captured_amount = amountMinor }, key, ct);
        using (response)
        {
            var id = HeaderId(response, "capture_id");
            return new KlarnaCapture(id, amountMinor);
        }
    }

    public async Task<string> RefundAsync(string orderId, long amountMinor, string key, CancellationToken ct)
    {
        PositiveAmount(amountMinor);
        using var response = await SendAsync(HttpMethod.Post, OrderPath(orderId) + "/refunds",
            new { refunded_amount = amountMinor }, key, ct);
        return HeaderId(response, "refund_id");
    }

    public async Task CancelOrderAsync(string orderId, string key, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, OrderPath(orderId) + "/cancel", null, key, ct);
    }

    public async Task DisableHppAsync(string sessionUrl, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Delete, ValidateHppUrl(sessionUrl), null, null, ct);
    }

    private async Task<JsonElement> SendJsonAsync(HttpMethod method, string path, object? body, string? key, CancellationToken ct)
    {
        using var response = await SendAsync(method, path, body, key, ct);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct);
        return document.RootElement.Clone();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Klarna playground credentials are not configured");
        using var request = new HttpRequestMessage(method, baseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (key != null)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 255 || key.Any(char.IsControl))
                throw new ArgumentException("Invalid Klarna idempotency key", nameof(key));
            request.Headers.Add("Klarna-Idempotency-Key", key);
        }
        if (body != null) request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return response;
        var code = response.StatusCode;
        response.Dispose();
        throw new HttpRequestException($"Klarna API returned {(int)code}", null, code);
    }

    private static string HeaderId(HttpResponseMessage response, string header)
    {
        if (response.Headers.TryGetValues(header, out var values))
        {
            var id = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(id)) return id;
        }
        throw new HttpRequestException($"Klarna success response lacks {header}");
    }

    private static string RequiredString(JsonElement value, string name) =>
        OptionalString(value, name) is { Length: > 0 } text ? text :
            throw new HttpRequestException($"Klarna response lacks {name}");

    private static string? OptionalString(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item)
        && item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    private static long RequiredInt64(JsonElement value, string name) =>
        value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number
        && item.TryGetInt64(out var result) ? result :
            throw new HttpRequestException($"Klarna response lacks {name}");

    private static string OrderPath(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Missing Klarna order ID", nameof(id));
        return "/ordermanagement/v1/orders/" + Uri.EscapeDataString(id);
    }

    private string ValidateHppUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.GetLeftPart(UriPartial.Authority) != baseUrl
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || !uri.AbsolutePath.StartsWith("/hpp/v1/sessions/", StringComparison.Ordinal)
            || uri.AbsolutePath["/hpp/v1/sessions/".Length..].Length == 0
            || uri.AbsolutePath["/hpp/v1/sessions/".Length..].Contains('/'))
            throw new ArgumentException("HPP session URL is not on the configured Klarna playground API", nameof(url));
        return uri.PathAndQuery;
    }

    private static string ValidateBaseUrl(string? url)
    {
        var trimmed = (url ?? "https://api-na.playground.klarna.com").TrimEnd('/');
        if (trimmed != "https://api-na.playground.klarna.com")
            throw new ArgumentException("US / USD demo requires the North American Klarna playground API origin");
        return trimmed;
    }

    private static string AppendPlaceholders(string url, bool success)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Klarna return URL must be HTTPS (or HTTP loopback) without credentials or fragments");
        return url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?")
            + "session_id={{session_id}}" + (success ? "&order_id={{order_id}}" : "");
    }

    private static void PositiveAmount(long amount)
    {
        if (amount is <= 0 or > 200_000_000)
            throw new ArgumentOutOfRangeException(nameof(amount), "Klarna amount must be positive minor units");
    }

    private static bool ValidUsRegion(string? value)
    {
        if (value is null) return false;
        var code = value.StartsWith("US-", StringComparison.Ordinal) ? value[3..] : value;
        return UsStates.Contains(code);
    }

    private static bool ValidUsPostalCode(string? value) =>
        string.IsNullOrWhiteSpace(value) || Regex.IsMatch(value, @"\A[0-9]{5}(-[0-9]{4})?\z");

    private static void ValidateCheckout(KlarnaCheckoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PurchaseCountry != "US" || request.Currency != "USD" || request.Locale != "en-US")
            throw new ArgumentException("This playground demo supports only US / USD / en-US");
        if (request.ShippingAddress is not { } address || address.CountryCode != "US"
            || string.IsNullOrWhiteSpace(address.FullName) || string.IsNullOrWhiteSpace(address.Street)
            || string.IsNullOrWhiteSpace(address.City) || !ValidUsRegion(address.Region)
            || !ValidUsPostalCode(address.PostalCode))
            throw new ArgumentException("A US shipping address with name, street, city, and region is required");
        if (string.IsNullOrWhiteSpace(request.MerchantReference) || request.MerchantReference.Length > 255)
            throw new ArgumentException("Invalid merchant reference");
        PositiveAmount(request.OrderAmountMinor);
        if (request.Lines is not { Count: > 0 and <= 1000 })
            throw new ArgumentException("Klarna requires 1–1000 order lines");
        long sum = 0;
        foreach (var line in request.Lines)
        {
            if (line.Type is not ("physical" or "shipping_fee" or "discount") || line.Quantity <= 0
                || (line.Type == "discount" ? line.UnitPriceMinor >= 0 : line.UnitPriceMinor <= 0)
                || string.IsNullOrWhiteSpace(line.Reference) || string.IsNullOrWhiteSpace(line.Name))
                throw new ArgumentException("Invalid Klarna physical, shipping fee, or negative discount line");
            sum = checked(sum + checked(line.UnitPriceMinor * line.Quantity));
        }
        if (sum != request.OrderAmountMinor)
            throw new ArgumentException("Klarna order amount does not equal the order-line sum");
        AppendPlaceholders(request.SuccessUrl, true);
        AppendPlaceholders(request.CancelUrl, false);
    }
}
