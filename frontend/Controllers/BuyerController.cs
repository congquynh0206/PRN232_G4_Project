using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace frontend.Controllers;

[Authorize(Roles = "buyer")]
public sealed class BuyerController(IHttpClientFactory factory, IConfiguration config) : Controller
{
    public IActionResult Index() => View();

    public IActionResult Products() => View();

    public IActionResult Checkout(int? product, string? checkout)
    {
        var session = Guid.TryParse(checkout, out var id) ? id.ToString() : null;
        var url = Url.Action(
            nameof(Index),
            null,
            new { tab = "checkout", product, checkout = session },
            null,
            null,
            "checkout"
        );
        return Redirect(url ?? "/Buyer?tab=checkout#checkout");
    }

    [HttpGet]
    public async Task<IActionResult> PayPalCartReturn(
        long checkoutId,
        string? token,
        CancellationToken ct
    )
    {
        if (checkoutId <= 0 || string.IsNullOrWhiteSpace(token))
            return RedirectToAction(nameof(Index));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BackendUrl($"api/buyer/paypal-checkouts/{checkoutId}/capture")
        );
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            User.FindFirstValue("access_token")
        );
        request.Content = System.Net.Http.Json.JsonContent.Create(new { providerOrderId = token });
        using var response = await factory.CreateClient().SendAsync(request, ct);
        return RedirectToAction(
            nameof(Index),
            new
            {
                paypalCheckoutId = checkoutId,
                paymentResult = response.IsSuccessStatusCode ? "checked"
                : (int)response.StatusCode == 429 ? "rate-limited"
                : "error",
                retryAfterSeconds = RetrySeconds(response),
            }
        );
    }

    [HttpGet]
    public IActionResult PayPalCartCancel(long checkoutId) =>
        RedirectToAction(
            nameof(Index),
            new { paypalCheckoutId = checkoutId, paymentResult = "cancelled" }
        );

    [HttpGet]
    public async Task<IActionResult> PayPalReturn(int orderId, string? token, CancellationToken ct)
    {
        if (orderId <= 0 || string.IsNullOrWhiteSpace(token))
            return RedirectToAction(nameof(Index));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BackendUrl($"api/orders/{orderId}/pay/paypal/{Uri.EscapeDataString(token)}/capture")
        );
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            User.FindFirstValue("access_token")
        );
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var response = await factory.CreateClient().SendAsync(request, ct);
        return RedirectToAction(
            nameof(Index),
            new
            {
                orderId,
                paymentResult = response.IsSuccessStatusCode ? "checked"
                : (int)response.StatusCode == 429 ? "rate-limited"
                : "error",
                retryAfterSeconds = RetrySeconds(response),
            }
        );
    }

    [HttpGet]
    public IActionResult PayPalCancel(int orderId) =>
        RedirectToAction(nameof(Index), new { orderId, paymentResult = "cancelled" });

    [HttpGet]
    public async Task<IActionResult> KlarnaReturn(int orderId, CancellationToken ct)
    {
        if (orderId <= 0)
            return RedirectToAction(nameof(Index));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BackendUrl($"api/orders/{orderId}/pay/klarna/reconcile")
        );
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            User.FindFirstValue("access_token")
        );
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var response = await factory.CreateClient().SendAsync(request, ct);
        return RedirectToAction(
            nameof(Index),
            new
            {
                orderId,
                paymentProvider = "klarna",
                paymentResult = response.IsSuccessStatusCode ? "checked"
                    : (int)response.StatusCode == 429 ? "rate-limited"
                    : "error",
                retryAfterSeconds = RetrySeconds(response),
            }
        );
    }

    [HttpGet]
    public async Task<IActionResult> KlarnaCancel(int orderId, CancellationToken ct)
    {
        if (orderId <= 0)
            return RedirectToAction(nameof(Index));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BackendUrl($"api/orders/{orderId}/pay/klarna/cancel")
        );
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            User.FindFirstValue("access_token")
        );
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var response = await factory.CreateClient().SendAsync(request, ct);
        // Failed: the session is disabled and the buyer may pick another method.
        // Any other status (e.g. Authorized after a late approval) is shown as a normal check.
        var status = response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<KlarnaAttempt>(ct))?.Status
            : null;
        return RedirectToAction(
            nameof(Index),
            new
            {
                orderId,
                paymentProvider = "klarna",
                paymentResult = status == "Failed" ? "cancelled"
                    : status != null ? "checked"
                    : (int)response.StatusCode == 429 ? "rate-limited"
                    : "error",
                retryAfterSeconds = RetrySeconds(response),
            }
        );
    }

    private sealed record KlarnaAttempt(string? Status);

    private string BackendUrl(string path) =>
        (config["ApiSettings:BaseUrl"] ?? "http://localhost:5251").TrimEnd('/') + "/" + path;

    private static int? RetrySeconds(HttpResponseMessage response) =>
        (int)response.StatusCode == 429
            ? Math.Clamp(
                (int)Math.Ceiling(response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 60),
                1,
                3600
            )
            : null;
}
