using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace frontend.Controllers;

[AllowAnonymous]
public sealed class AccountController(IHttpClientFactory factory, IConfiguration configuration) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectForRole(User.FindFirstValue(ClaimTypes.Role));
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl, CancellationToken ct)
    {
        using var response = await factory.CreateClient().PostAsync(BackendUrl("api/auth/login"),
            new StringContent(JsonSerializer.Serialize(new { email, password }), Encoding.UTF8, "application/json"), ct);
        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError("", "Email hoặc mật khẩu không đúng.");
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }
        var result = await response.Content.ReadFromJsonAsync<LoginResult>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Login response is empty");
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, result.UserId.ToString()),
            new Claim(ClaimTypes.Name, result.Username),
            new Claim(ClaimTypes.Role, result.Role),
            new Claim("access_token", result.Token)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = result.ExpiresAt });
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
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

    private IActionResult RedirectForRole(string? role) => role switch
    {
        "buyer" => RedirectToAction("Index", "Buyer"),
        "seller" => RedirectToAction("Index", "Seller"),
        "shipper" => RedirectToAction("Index", "Shipper"),
        "admin" => RedirectToAction("Index", "Admin"),
        _ => RedirectToAction(nameof(AccessDenied))
    };

    private string BackendUrl(string path) =>
        (configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5251").TrimEnd('/') + "/" + path;

    private sealed record LoginResult(string Token, DateTime ExpiresAt, int UserId, string Username, string Role);
}
