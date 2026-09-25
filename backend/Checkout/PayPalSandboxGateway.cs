using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace backend.Checkout;

public sealed class PayPalSandboxGateway(HttpClient http, IConfiguration configuration) : IPayPalGateway
{
    private readonly string _clientId = configuration["PayPal:ClientId"] ?? "";
    private readonly string _clientSecret = configuration["PayPal:ClientSecret"] ?? "";
    private readonly string _baseUrl = configuration["PayPal:BaseUrl"] ?? "https://api-m.sandbox.paypal.com";

    public async Task<PayPalCreated> CreateAsync(decimal amount, string currency, string requestId,
        string returnUrl, string cancelUrl, CancellationToken ct)
    {
        var body = new
        {
            intent = "CAPTURE",
            purchase_units = new[] { new { amount = new { currency_code = currency, value = amount.ToString("F2", CultureInfo.InvariantCulture) } } },
            payment_source = new { paypal = new { experience_context = new
            {
                return_url = returnUrl, cancel_url = cancelUrl, user_action = "PAY_NOW"
            } } }
        };
        using var response = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", requestId, body, ct);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var id = json.RootElement.GetProperty("id").GetString() ?? throw new HttpRequestException("PayPal order ID missing");
        var links = json.RootElement.GetProperty("links").EnumerateArray();
        var approve = links.FirstOrDefault(link => link.GetProperty("rel").GetString() is "approve" or "payer-action");
        var url = approve.ValueKind == JsonValueKind.Undefined ? null : approve.GetProperty("href").GetString();
        return new PayPalCreated(id, url ?? throw new HttpRequestException("PayPal approval URL missing"));
    }

    public async Task<PayPalCaptured> CaptureAsync(string orderId, string requestId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/capture", requestId, new { }, ct);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return ParseCapture(json.RootElement);
    }

    public async Task<PayPalCaptured> GetAsync(string orderId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}", null, null, ct);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return ParseCapture(json.RootElement);
    }

    public async Task<string> RefundAsync(string captureId, decimal amount, string currency, string requestId, CancellationToken ct)
    {
        var body = new { amount = new { currency_code = currency, value = amount.ToString("F2", CultureInfo.InvariantCulture) } };
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", requestId, body, ct);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var status = json.RootElement.GetProperty("status").GetString();
        if (status != "COMPLETED") throw new HttpRequestException("PayPal refund is not completed");
        return json.RootElement.GetProperty("id").GetString() ?? throw new HttpRequestException("PayPal refund ID missing");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? requestId, object? body, CancellationToken ct)
    {
        var token = await AccessTokenAsync(ct);
        using var request = new HttpRequestMessage(method, _baseUrl.TrimEnd('/') + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (requestId is not null) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        if (body is not null) request.Content = JsonContent.Create(body);
        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"PayPal API returned {status}");
        }
        return response;
    }

    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
            throw new InvalidOperationException("PayPal sandbox credentials are not configured");
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl.TrimEnd('/') + "/v1/oauth2/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(_clientId + ":" + _clientSecret)));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return json.RootElement.GetProperty("access_token").GetString() ?? throw new HttpRequestException("PayPal token missing");
    }

    private static PayPalCaptured ParseCapture(JsonElement root)
    {
        var status = root.GetProperty("status").GetString() ?? "UNKNOWN";
        if (!root.TryGetProperty("purchase_units", out var units)) return new PayPalCaptured(status, null, 0m, "");
        var unit = units.EnumerateArray().First();
        if (!unit.TryGetProperty("payments", out var payments) || !payments.TryGetProperty("captures", out var captures))
            return new PayPalCaptured(status, null, 0m, "");
        var capture = captures.EnumerateArray().FirstOrDefault();
        if (capture.ValueKind == JsonValueKind.Undefined) return new PayPalCaptured(status, null, 0m, "");
        var money = capture.GetProperty("amount");
        return new PayPalCaptured(status, capture.GetProperty("id").GetString(),
            decimal.Parse(money.GetProperty("value").GetString()!, CultureInfo.InvariantCulture),
            money.GetProperty("currency_code").GetString() ?? "");
    }
}
