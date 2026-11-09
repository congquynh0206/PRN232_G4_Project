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
            JsonSerializer.Serialize(new
            {
                email,
                password
            }),
            Encoding.UTF8,
            "application/json"
        );
        TrustedProxyHeaders.ForwardClientIp(request, HttpContext);
        using var response = await factory.CreateClient().SendAsync(request, ct);
        TrustedProxyHeaders.CopyRetryAfter(response, Response);
        if ((int)response.StatusCode == StatusCodes.Status429TooManyRequests)
        {
            ShowRateLimit(response);
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }
        if ((int)response.StatusCode == StatusCodes.Status403Forbidden)
        {
            // The API only returns 403 here for a locked account; show its safe message.
            var denied = await response.Content.ReadAsStringAsync(ct);
            var message = "Tài khoản không thể đăng nhập.";
            try
            {
                using var document = JsonDocument.Parse(denied);
                if (document.RootElement.TryGetProperty("error", out var error))
                    message = error.GetString() ?? message;
            }
            catch (JsonException) { }
            ModelState.AddModelError("", message);
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }
        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError("", "Email hoặc mật khẩu không đúng.");
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        var result = await SignInAsync(response, ct);
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);
        return RedirectForRole(result.Role);
    }

    [HttpGet]
    public IActionResult RegisterBuyer()
    {
        if (!environment.IsDevelopment())
            return NotFound();
        if (User.Identity?.IsAuthenticated == true)
            return RedirectForRole(User.FindFirstValue(ClaimTypes.Role));
        return View();
    }

    // Development only, like seller registration. A new buyer is signed in right away.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(CommerceRateLimiting.Registration)]
    public async Task<IActionResult> RegisterBuyer(
        string firstName,
        string lastName,
        string email,
        string password,
        CancellationToken ct
    )
    {
        if (!environment.IsDevelopment())
            return NotFound();
        ViewBag.FirstName = firstName;
        ViewBag.LastName = lastName;
        ViewBag.Email = email;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BackendUrl("api/auth/register-buyer")
        );
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { firstName, lastName, email, password }),
            Encoding.UTF8,
            "application/json"
        );
        TrustedProxyHeaders.ForwardClientIp(request, HttpContext);
        HttpResponseMessage response;
        try
        {
            response = await factory.CreateClient().SendAsync(request, ct);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested
            )
        {
            ModelState.AddModelError(
                "",
                "Chưa xác định được kết quả đăng ký. Hãy thử đăng nhập bằng email này trước khi gửi lại."
            );
            return View();
        }
        using (response)
        {
            TrustedProxyHeaders.CopyRetryAfter(response, Response);
            if ((int)response.StatusCode == StatusCodes.Status429TooManyRequests)
            {
                ShowRateLimit(response);
                return View();
            }
            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", await ErrorMessage(response, ct));
                return View();
            }
            await SignInAsync(response, ct);
            return RedirectToAction("Index", "Buyer");
        }
    }

    private void ShowRateLimit(HttpResponseMessage response)
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
    }

    private static async Task<string> ErrorMessage(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        const string fallback = "Không thể đăng ký. Hãy kiểm tra lại thông tin.";
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return document.RootElement.TryGetProperty("error", out var error)
                ? error.GetString() ?? fallback
                : fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private async Task<LoginResult> SignInAsync(HttpResponseMessage response, CancellationToken ct)
    {
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
        return result;
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
