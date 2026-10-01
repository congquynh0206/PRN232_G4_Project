using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace frontend.Controllers;

[Authorize(Roles = "buyer")]
public sealed class BuyerController(IHttpClientFactory factory, IConfiguration config) : Controller
{
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> PayPalReturn(int orderId, string? token, CancellationToken ct)
    {
        if (orderId <= 0 || string.IsNullOrWhiteSpace(token)) return RedirectToAction(nameof(Index));
        using var request = new HttpRequestMessage(HttpMethod.Post,
            BackendUrl($"api/orders/{orderId}/pay/paypal/{Uri.EscapeDataString(token)}/capture"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", User.FindFirstValue("access_token"));
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var response = await factory.CreateClient().SendAsync(request, ct);
        return RedirectToAction(nameof(Index), new { orderId, paymentResult = response.IsSuccessStatusCode ? "checked" : "error" });
    }

    [HttpGet]
    public IActionResult PayPalCancel(int orderId) =>
        RedirectToAction(nameof(Index), new { orderId, paymentResult = "cancelled" });

    private string BackendUrl(string path) =>
        (config["ApiSettings:BaseUrl"] ?? "http://localhost:5251").TrimEnd('/') + "/" + path;
}
