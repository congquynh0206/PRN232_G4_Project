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
public sealed class AuthenticationController(
    IAuthenticationService authentication,
    IBuyerRegistrationService buyerRegistration,
    IConfiguration configuration,
    IHostEnvironment environment
) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await authentication.ValidateCredentialsAsync(
            request.Email,
            request.Password,
            ct
        );
        if (user is null || string.IsNullOrWhiteSpace(user.Role))
            return Unauthorized(new
            {
                error = "Email hoặc mật khẩu không đúng"
            });
        if (user.BannedAt != null)
            return StatusCode(
                403,
                new
                {
                    error = user.BanReason ?? "Tài khoản đã bị khóa vĩnh viễn."
                }
            );
        var role = user.Role.ToLowerInvariant();
        if (role is not ("buyer" or "seller" or "admin" or "shipper"))
            return StatusCode(403, new
            {
                error = "Tài khoản chưa được gán role hợp lệ"
            });
        return Ok(Issue(user, role));
    }

    // Development only, like seller registration. Returns a login token so the
    // new buyer is signed in right away.
    [HttpPost("register-buyer")]
    public async Task<IActionResult> RegisterBuyer(
        BuyerRegistrationInput input,
        CancellationToken ct
    )
    {
        if (!environment.IsDevelopment())
            return NotFound();
        var user = await buyerRegistration.RegisterAsync(input, ct);
        return Ok(Issue(user, "buyer"));
    }

    private LoginResponse Issue(User user, string role)
    {
        var expiresAt = DateTime.UtcNow.AddHours(8);
        var key =
            configuration["Authentication:JwtKey"]
            ?? throw new InvalidOperationException("Authentication JWT key is not configured");
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username ?? user.Email ?? user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email ?? ""),
            new Claim(ClaimTypes.Role, role),
        };
        var token = new JwtSecurityToken(
            issuer: configuration["Authentication:Issuer"],
            audience: configuration["Authentication:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256
            )
        );
        return new LoginResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt,
            user.Id,
            user.Username ?? user.Email ?? $"User {user.Id}",
            role
        );
    }
}
