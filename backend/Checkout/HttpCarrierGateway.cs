using System.Net.Http.Json;
using System.Text.Json;

namespace backend.Checkout;

public sealed class HttpCarrierGateway(HttpClient http, IConfiguration config) : ICarrierGateway
{
    public async Task<string> CreateLabelAsync(int orderId, string direction, string key, CancellationToken ct)
    {
        var baseUrl = (config["Carrier:BaseUrl"] ?? "http://localhost:5251").TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/carrier/labels");
        request.Headers.Add("X-Carrier-Key", config["Carrier:Key"] ?? "demo-only-carrier-key");
        request.Content = JsonContent.Create(new { orderId, direction, key });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Carrier returned {response.StatusCode}");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return body.RootElement.GetProperty("trackingNumber").GetString() ?? throw new HttpRequestException("Tracking number missing");
    }
}
