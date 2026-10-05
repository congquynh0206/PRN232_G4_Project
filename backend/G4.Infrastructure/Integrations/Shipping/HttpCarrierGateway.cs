using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using G4.Infrastructure.Diagnostics;

namespace G4.Infrastructure.Integrations.Shipping;

public sealed class HttpCarrierGateway(HttpClient http, IConfiguration config, IIntegrationLogWriter? logs = null) : ICarrierGateway
{
    public async Task<string> CreateLabelAsync(int orderId, string direction, string key, CancellationToken ct)
    {
        var baseUrl = (config["Carrier:BaseUrl"] ?? "http://localhost:5251").TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/carrier/labels");
        request.Headers.Add("X-Carrier-Key", config["Carrier:Key"] ?? "local-carrier-key");
        request.Content = JsonContent.Create(new { orderId, direction, key });
        using var context = IntegrationContext.ForOrder(orderId, IntegrationContext.Current.Attempt);
        await using var call = new IntegrationCallRecorder(logs).Start("Carrier", "CreateLabel" + direction, "Simulated");
        try
        {
            using var response = await http.SendAsync(request, ct);
            call.HttpStatus = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Carrier returned {response.StatusCode}", null, response.StatusCode);
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var tracking = body.RootElement.GetProperty("trackingNumber").GetString() ?? throw new HttpRequestException("Tracking number missing");
            await call.CompleteAsync("Succeeded", providerReference: tracking, ct: ct);
            return tracking;
        }
        catch (Exception ex) { await call.FailAsync(ex, ct); throw; }
    }
}
