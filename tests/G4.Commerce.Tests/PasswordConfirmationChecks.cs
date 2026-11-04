using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using G4.Api.Controllers;
using G4.Api.Security;
using G4.Application.Services;
using G4.Domain.Entities;
using G4.Infrastructure.Authentication;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Disputes;
using G4.Infrastructure.Checkout;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

internal static class PasswordConfirmationChecks
{
    private const string SigningKey = "password-confirmation-http-fixture-signing-key-2026";
    private const string Password = "Mật khẩu 123456!";

    public static async Task RunAsync()
    {
        await using var app = await StartAsync();
        using var client = new HttpClient
        {
            BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single()),
        };
        var before = await SnapshotAsync(app);
        // Exercise actual MVC routing/authorization, including legacy and simulator entry points.
        var actions = app.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
            .Where(x => x.MethodInfo.IsDefined(typeof(RequirePasswordAttribute), false)).ToArray();
        Check(actions.Length == 21, "all agreed existing entry points are protected");
        foreach (var action in actions)
        {
            var policy = (RequirePasswordAttribute)Attribute.GetCustomAttribute(
                action.MethodInfo, typeof(RequirePasswordAttribute))!;
            var role = policy.Role.Split(',')[0];
            var template = action.AttributeRouteInfo!.Template!.Replace("{role}", role);
            var path = "/" + Regex.Replace(template, "\\{[^}]+\\}", "1");
            using var response = await SendAsync(client, path, role == "admin" ? 31 : 21, role);
            await ExpectAsync(response, 428, "PasswordConfirmationRequired");
        }
        Check(await SnapshotAsync(app) == before, "missing confirmation cannot mutate any data");
        using (var anonymous = await client.PostAsJsonAsync("/api/seller/payouts", new
        {
        }))
            Check((int)anonymous.StatusCode == 401, "anonymous remains unauthorized");
        using (var wrongRole = await SendAsync(
            client, "/api/seller/payouts", 11, "buyer", Password))
            Check((int)wrongRole.StatusCode == 403, "password cannot grant a different role");
        using (var wrong = await SendAsync(client, "/api/seller/payouts", 21, "seller", "wrong"))
            await ExpectAsync(wrong, 403, "PasswordConfirmationInvalid");
        using (var other = await SendAsync(client, "/api/seller/payouts", 22, "seller", Password))
            await ExpectAsync(other, 403, "PasswordConfirmationInvalid");
        using (var staleRole = await SendAsync(
            client, "/api/seller/payouts", 31, "seller", Password))
            await ExpectAsync(staleRole, 403, "PasswordConfirmationInvalid");
        Check(await SnapshotAsync(app) == before, "wrong password/role never debits wallet");

        var payout = new
        {
            amount = 12.50m,
            key = "password-payout-once",
            simulateFailure = false
        };
        for (var i = 0; i < 2; i++)
        {
            using var success = await SendAsync(
                client, "/api/seller/payouts", 21, "seller", Password, payout);
            await ExpectAsync(success, 200);
        }
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Check(await db.SellerPayouts.CountAsync() == 1, "same key creates one payout");
            var account = await db.SellerAccounts.SingleAsync(x => x.SellerId == 21);
            Check(account.AvailableBalance == 87.50m,
                "confirmed payout debits only once");
        }
        using (var again = await SendAsync(client, "/api/seller/payouts", 21, "seller"))
            await ExpectAsync(again, 428, "PasswordConfirmationRequired");
        using (var owner = await SendAsync(
            client, "/api/seller/orders/999/refund", 21, "seller", Password,
            new
            {
                reason = "cancel"
            }))
            await ExpectAsync(owner, 403);
        using (var admin = await SendAsync(
            client, "/api/admin/platform-fees", 31, "admin", Password,
            new
            {
                feePercent = 10m,
                fixedFee = .30m,
                reason = "QA",
                requestKey = "password-fee"
            }))
            await ExpectAsync(admin, 200);

        before = await SnapshotAsync(app);
        var paths = new[] { "/api/seller/payouts", "/api/seller/shipping/address-change-cap" };
        var parallel = await Task.WhenAll(Enumerable.Range(0, 9).Select(i =>
            SendAsync(client, paths[i % 2], 24, "seller", "wrong")));
        Check(parallel.Count(x => (int)x.StatusCode == 403) == 5,
            "only five parallel guesses admitted");
        Check(parallel.Count(x => (int)x.StatusCode == 429) == 4,
            "shared per-user quota across actions");
        foreach (var response in parallel)
            response.Dispose();
        using (var blocked = await SendAsync(client, paths[0], 24, "seller", Password, payout))
        {
            await ExpectAsync(blocked, 429, "RateLimitExceeded");
            Check(blocked.Headers.RetryAfter?.Delta > TimeSpan.Zero, "actionable retry header");
        }
        Check(await SnapshotAsync(app) == before, "throttled attempts do not change finance");
        using (var otherUser = await SendAsync(client, paths[0], 25, "seller", Password,
            new
            {
                amount = 1,
                key = "other-user",
                simulateFailure = false
            }))
            await ExpectAsync(otherUser, 200);
        using (var get = new HttpRequestMessage(HttpMethod.Get, "/api/seller/finance"))
        {
            get.Headers.Authorization = Authorization(21, "seller");
            using var response = await client.SendAsync(get);
            await ExpectAsync(response, 200);
        }
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(x => x.Id == 25);
            user.Password = Hash("changed-password");
            await db.SaveChangesAsync();
        }
        using (var changed = await SendAsync(client, paths[0], 25, "seller", Password, payout))
            await ExpectAsync(changed, 403, "PasswordConfirmationInvalid");
        await app.StopAsync();
        Console.WriteLine("Password confirmation HTTP passed: 21 routes, identity/role/ownership, "
            + "Unicode password, no mutation on rejection, payout replay, admin fee, "
            + "parallel shared throttling, user isolation and changed password.");
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = typeof(PaymentsController).Assembly.FullName,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddControllers().AddApplicationPart(typeof(PaymentsController).Assembly);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            });
        builder.Services.AddAuthorization(options => options.FallbackPolicy =
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        var database = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(database));
        builder.Services.AddSingleton<PasswordAttemptLimiter>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
        builder.Services.AddScoped<ISellerFinanceService, SellerFinanceService>();
        builder.Services.AddScoped<IDisputeService, DisputeService>();
        builder.Services.AddScoped<ICheckoutService, CheckoutService>();
        builder.Services.AddScoped<IPayPalPaymentService, UnusedPayPal>();
        builder.Services.AddScoped<SellerFinanceReportService>();
        builder.Services.AddScoped<IReturnService>(_ => new ReturnService(null!, null!, null!));
        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        foreach (var id in new[] { 21, 22, 24, 25, 31 })
        {
            db.Users.Add(new User
            {
                Id = id,
                Role = id == 31 ? "admin" : "seller",
                Password = Hash(id == 22 ? "other-password" : Password)
            });
            if (id != 31)
                db.SellerAccounts.Add(new SellerAccount
                {
                    SellerId = id,
                    Level = 3,
                    AvailableBalance = 100,
                    MonthlySalesLimit = 1000000
                });
        }
        await db.SaveChangesAsync();
        return app;
    }

    internal static string Hash(string password)
    {
        var salt = Encoding.UTF8.GetBytes("password-confirmation-fixture");
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static AuthenticationHeaderValue Authorization(int id, string role)
    {
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                new Claim(ClaimTypes.Role, role),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256
            )
        );
        return new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string path,
        int id,
        string role,
        string? password = null,
        object? body = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body ?? new { }),
        };
        request.Headers.Authorization = Authorization(id, role);
        if (password != null)
            request.Headers.Add(
                RequirePasswordAttribute.HeaderName, Uri.EscapeDataString(password));
        return await client.SendAsync(request);
    }

    private static async Task<string> SnapshotAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return JsonSerializer.Serialize(new
        {
            accounts = await db.SellerAccounts.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            payouts = await db.SellerPayouts.CountAsync(),
            ledger = await db.FinancialTransactions.CountAsync(),
            outbox = await db.NotificationOutbox.CountAsync(),
            fees = await db.PlatformFeePolicies.CountAsync(),
        });
    }

    private static async Task ExpectAsync(
        HttpResponseMessage response,
        int status,
        string? code = null
    )
    {
        var body = await response.Content.ReadAsStringAsync();
        Check((int)response.StatusCode == status,
            $"expected {status}, got {response.StatusCode}: {body}");
        if (code != null)
            Check(JsonDocument.Parse(body).RootElement.GetProperty("code").GetString() == code,
                code);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class UnusedPayPal : IPayPalPaymentService
    {
        public Task<PayPalCreated> StartAsync(
            int orderId,
            string key,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Payment> CaptureAsync(int orderId, string providerId,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Payment> ReconcileAsync(int orderId,
            CancellationToken ct = default) => throw new NotSupportedException();
    }
}
