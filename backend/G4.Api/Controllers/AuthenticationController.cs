using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace G4.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthenticationController(IAuthenticationService authentication, IConfiguration configuration) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await authentication.ValidateCredentialsAsync(request.Email, request.Password, ct);
        if (user is null || string.IsNullOrWhiteSpace(user.Role)) return Unauthorized(new { error = "Email hoặc mật khẩu không đúng" });
        var role = user.Role.ToLowerInvariant();
        if (role is not ("buyer" or "seller" or "admin" or "shipper"))
            return StatusCode(403, new { error = "Tài khoản chưa được gán role hợp lệ" });
        var expiresAt = DateTime.UtcNow.AddHours(8);
        var key = configuration["Authentication:JwtKey"]
            ?? throw new InvalidOperationException("Authentication JWT key is not configured");
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username ?? user.Email ?? user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email ?? ""),
            new Claim(ClaimTypes.Role, role)
        };
        var token = new JwtSecurityToken(
            issuer: configuration["Authentication:Issuer"],
            audience: configuration["Authentication:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        return Ok(new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expiresAt, user.Id,
            user.Username ?? user.Email ?? $"User {user.Id}", role));
    }
}
