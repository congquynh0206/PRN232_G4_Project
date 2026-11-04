using System.Security.Claims;
using System.Text;
using System.Text.Json;
using G4.WebCommon;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace frontend.Controllers;

[AllowAnonymous]
public sealed class AccountController(
    IHttpClientFactory factory,
    IConfiguration configuration,
    IHostEnvironment environment
) : Controller
{
    [HttpGet]
    public IActionResult RegisterSeller() => environment.IsDevelopment() ? View() : NotFound();

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RegistrationSamples(CancellationToken ct)
    {
        if (!environment.IsDevelopment())
            return NotFound();
        using var reply = await factory
            .CreateClient()
            .GetAsync(BackendUrl("api/auth/seller-registration-samples"), ct);
        return new ContentResult
        {
            StatusCode = (int)reply.StatusCode,
            ContentType = "application/json",
            Content = await reply.Content.ReadAsStringAsync(ct),
        };
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(CommerceRateLimiting.Registration)]
    public async Task<IActionResult> RegisterSeller(
        [FromBody] JsonElement input,
        CancellationToken ct
    )
    {
        if (!environment.IsDevelopment())
            return NotFound();
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                BackendUrl("api/auth/register-seller")
            );
            request.Content = new StringContent(
                input.GetRawText(),
                Encoding.UTF8,
                "application/json"
            );
            TrustedProxyHeaders.ForwardClientIp(request, HttpContext);
            using var reply = await factory.CreateClient().SendAsync(request, ct);
            TrustedProxyHeaders.CopyRetryAfter(reply, Response);
            return new ContentResult
            {
                StatusCode = (int)reply.StatusCode,
                ContentType = "application/json",
                Content = await reply.Content.ReadAsStringAsync(ct),
            };
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested
            )
        {
            return StatusCode(
                503,
                new
                {
                    error = "Chưa xác định được kết quả đăng ký. Hãy kiểm tra kết nối rồi gửi lại đúng thông tin.",
                }
            );
        }
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectForRole(User.FindFirstValue(ClaimTypes.Role));
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(CommerceRateLimiting.Login)]
    public async Task<IActionResult> Login(
        string email,
        string password,
        string? returnUrl,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BackendUrl("api/auth/login"));
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { email, password }),
            Encoding.UTF8,
            "application/json"
        );
        TrustedProxyHeaders.ForwardClientIp(request, HttpContext);
        using var response = await factory.CreateClient().SendAsync(request, ct);
        TrustedProxyHeaders.CopyRetryAfter(response, Response);
        if ((int)response.StatusCode == StatusCodes.Status429TooManyRequests)
        {
            var retry = response.Headers.RetryAfter;
            var seconds = Math.Max(
                1,
                (int)
                    Math.Ceiling(
                        (
                            retry?.Delta
                            ?? (retry?.Date - DateTimeOffset.UtcNow)
                            ?? TimeSpan.FromSeconds(60)
                        ).TotalSeconds
                    )
            );
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ViewBag.RetryAfterSeconds = seconds;
            ModelState.AddModelError("", CommerceRateLimiting.Message(seconds));
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }
        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError("", "Email hoặc mật khẩu không đúng.");
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        var result =
            await response.Content.ReadFromJsonAsync<LoginResult>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Login response is empty");
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, result.UserId.ToString()),
            new Claim(ClaimTypes.Name, result.Username),
            new Claim(ClaimTypes.Role, result.Role),
            new Claim("access_token", result.Token),
        };
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(
                new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)
            ),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = result.ExpiresAt }
        );
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);
        return RedirectForRole(result.Role);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    private IActionResult RedirectForRole(string? role) =>
        role switch
        {
            "buyer" => RedirectToAction("Index", "Buyer"),
            "seller" => RedirectToAction("Index", "Seller"),
            "shipper" => RedirectToAction("Index", "Shipper"),
            "admin" => RedirectToAction("Index", "Admin"),
            _ => RedirectToAction(nameof(AccessDenied)),
        };

    private string BackendUrl(string path) =>
        (configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5251").TrimEnd('/') + "/" + path;

    private sealed record LoginResult(
        string Token,
        DateTime ExpiresAt,
        int UserId,
        string Username,
        string Role
    );
}
