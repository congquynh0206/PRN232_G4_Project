using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;

namespace frontend.Controllers;

public sealed class CheckoutController(IHttpClientFactory factory, IConfiguration config) : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> PayPalReturn(int orderId, string? token, CancellationToken ct)
    {
        if (orderId <= 0 || string.IsNullOrWhiteSpace(token)) return RedirectToAction(nameof(Index));
        using var request = new HttpRequestMessage(HttpMethod.Post,
            BackendUrl($"api/orders/{orderId}/pay/paypal/{Uri.EscapeDataString(token)}/capture"));
        request.Headers.Add("X-Actor-Role", "buyer");
        using var response = await factory.CreateClient().SendAsync(request, ct);
        return RedirectToAction(nameof(Index), new { orderId, paymentResult = response.IsSuccessStatusCode ? "checked" : "error" });
    }

    [HttpGet]
    public IActionResult PayPalCancel(int orderId) =>
        RedirectToAction(nameof(Index), new { orderId, paymentResult = "cancelled" });

    [AcceptVerbs("GET", "POST")]
    [Route("api/proxy/{**path}")]
    public async Task<IActionResult> Api(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal)) return BadRequest();
        using var request = new HttpRequestMessage(new HttpMethod(Request.Method), BackendUrl("api/" + path + Request.QueryString));
        var role = Request.Headers["X-Actor-Role"].ToString();
        request.Headers.Add("X-Actor-Role", role is "buyer" or "seller" ? role : "buyer");
        if (Request.Method == "POST")
        {
            request.Content = new StreamContent(Request.Body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        using var response = await factory.CreateClient().SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return new ContentResult
        {
            Content = body,
            ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
            StatusCode = (int)response.StatusCode
        };
    }

    private string BackendUrl(string path) =>
        (config["ApiSettings:BaseUrl"] ?? "http://localhost:5251").TrimEnd('/') + "/" + path;
}
