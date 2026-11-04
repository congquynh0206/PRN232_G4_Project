using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using G4.Api.Controllers;
using G4.Api.RateLimiting;
using G4.Api.Security;
using G4.Infrastructure.Authentication;
using G4.Application.Services;
using G4.Domain.Entities;
using G4.Infrastructure.Disputes;
using G4.WebCommon;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

internal static class RateLimitChecks
{
    private const string SigningKey = "rate-limit-fixture-only-32-byte-signing-key-2026";

    public static async Task RunAsync()
    {
        await using var app = await StartAsync();
        using var client = new HttpClient { BaseAddress = Address(app) };
        await PolicyCoverageAsync(app);
        await MoneyGuardsAsync(app, client);
        await IdentityAndIpAsync(client);
        await ConcurrentAsync(client);
        await RecoveryAsync();
        await app.StopAsync();
        Console.WriteLine(
            "Rate limits HTTP passed: actual MVC policy coverage, grouped checkout/card/PayPal, "
                + "payout/refund guards with unchanged money/stock/outbox, JWT identity isolation, "
                + "trusted/untrusted proxy IP, parallel admission, 429/Retry-After and recovery."
        );
    }

    private static async Task<WebApplication> StartAsync(int readWindow = 30)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = "Development",
                ApplicationName = typeof(PaymentsController).Assembly.FullName,
            }
        );
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var settings = new Dictionary<string, string?>
        {
            ["Finance:Levels:3:MonthlySalesLimit"] = "1000000",
            ["Finance:Levels:3:HoldDays"] = "0",
            ["TrustedProxies:Addresses:0"] = "127.0.0.1",
            ["TrustedProxies:Addresses:1"] = "::1",
        };
        foreach (
            var group in new[] { "login", "registration", "read", "write", "payment", "finance" }
        )
        {
            settings[$"RateLimiting:{group}:PermitLimit"] = "2";
            settings[$"RateLimiting:{group}:WindowSeconds"] = "30";
            settings[$"RateLimiting:{group}:SegmentsPerWindow"] = "2";
        }
        settings["RateLimiting:read:WindowSeconds"] = readWindow.ToString();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddTrustedProxyHeaders(builder.Configuration);
        builder.Services.AddCommerceRateLimiting(
            builder.Configuration,
            skipRequest: RequirePasswordAttribute.IsChallenge
        );
        builder
            .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = "rate-limit-tests",
                    ValidAudience = "rate-limit-tests",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                };
            });
        builder.Services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build()
        );
        builder
            .Services.AddControllers(options =>
                options.Conventions.Add(new CommerceRateLimitConvention())
            )
            .AddApplicationPart(typeof(PaymentsController).Assembly);
        var database = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(database)
        );
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<PasswordAttemptLimiter>();
        builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
        builder.Services.AddScoped<ICheckoutService, CheckoutService>();
        builder.Services.AddScoped<ISellerFinanceService, SellerFinanceService>();
        builder.Services.AddScoped<IReturnService>(sp => new ReturnService(
            sp.GetRequiredService<ApplicationDbContext>(),
            new TestRefundGateway(),
            new TestCarrier()
        ));
        builder.Services.AddScoped<IDisputeService, DisputeService>();
        builder.Services.AddScoped<SellerFinanceReportService>();
        builder.Services.AddScoped<IPayPalPaymentService, NeverPayPal>();
        var app = builder.Build();
        // Only this fixture can override the transport peer, to exercise an untrusted proxy.
        app.Use(
            async (context, next) =>
            {
                if (IPAddress.TryParse(context.Request.Headers["X-Test-Peer"], out var peer))
                    context.Connection.RemoteIpAddress = peer;
                await next(context);
            }
        );
        app.UseTrustedProxyHeaders(builder.Configuration);
        app.UseRouting();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapControllers();
        foreach (
            var group in new[] { "login", "registration", "read", "write", "payment", "finance" }
        )
        {
            var endpoint = app.MapPost(
                    "/probe/" + group,
                    (HttpContext context) =>
                        Results.Ok(
                            new
                            {
                                identity = CommerceRateLimiting.Identity(
                                    context,
                                    group is "login" or "registration"
                                ),
                            }
                        )
                )
                .RequireRateLimiting(group);
            if (group is "login" or "registration")
                endpoint.AllowAnonymous();
        }
        await app.StartAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Users.AddRange(
            new User { Id = 11, Role = "buyer" },
            new User { Id = 21, Role = "seller", Password = PasswordConfirmationChecks.Hash("rate-test-password") }
        );
        db.Addresses.AddRange(
            new Address
            {
                Id = 1,
                UserId = 11,
                State = "Hanoi",
                Country = "Vietnam",
            },
            new Address
            {
                Id = 2,
                UserId = 21,
                State = "Hanoi",
                Country = "Vietnam",
                IsDefault = true,
            }
        );
        db.Products.Add(
            new Product
            {
                Id = 1,
                SellerId = 21,
                Price = 10,
                WeightKg = 1,
                Title = "Item",
            }
        );
        db.Inventories.Add(
            new Inventory
            {
                Id = 1,
                ProductId = 1,
                Quantity = 20,
            }
        );
        db.SellerAccounts.Add(
            new SellerAccount
            {
                Id = 1,
                SellerId = 21,
                Level = 3,
                Status = "Active",
                AvailableBalance = 100,
                MonthlySalesLimit = 1000000,
            }
        );
        await db.SaveChangesAsync();
        return app;
    }

    private static async Task PolicyCoverageAsync(WebApplication app)
    {
        var actions = app
            .Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
            .ToArray();
        foreach (var action in actions)
        {
            var policy = action
                .EndpointMetadata.OfType<EnableRateLimitingAttribute>()
                .SingleOrDefault();
            Check(
                action.ControllerName == "CarrierSimulator" ? policy is null : policy is not null,
                "every user API has a policy; carrier is separate"
            );
        }
        foreach (
            var pair in new[]
            {
                ("Authentication", "Login", "login"),
                ("SellerRegistration", "Register", "registration"),
                ("SellerRegistration", "Samples", "read"),
                ("Finance", "Summary", "read"),
                ("Payments", "PayCard", "payment"),
                ("Payments", "StartPayPal", "payment"),
                ("PayPalCart", "Capture", "payment"),
                ("Checkout", "CreateOrder", "payment"),
                ("Cart", "Checkout", "payment"),
                ("Payments", "Refund", "finance"),
                ("Finance", "RequestPayout", "finance"),
                ("Finance", "ResolveHold", "finance"),
                ("Disputes", "Resolution", "finance"),
                ("Disputes", "SubmitResponse", "finance"),
            }
        )
        {
            var action = actions.Single(x =>
                x.ControllerName == pair.Item1 && x.ActionName == pair.Item2
            );
            Check(
                action.EndpointMetadata.OfType<EnableRateLimitingAttribute>().Single().PolicyName
                    == pair.Item3,
                $"policy covers money entry point {pair.Item1}.{pair.Item2}"
            );
        }
        await Task.CompletedTask;
    }

    private static async Task MoneyGuardsAsync(WebApplication app, HttpClient client)
    {
        using var created = await SendAsync(
            client,
            "/api/orders",
            11,
            "buyer",
            new
            {
                addressId = 1,
                items = new[] { new { productId = 1, quantity = 1 } },
                checkoutKey = "rate-order",
            }
        );
        await ExpectAsync(created, HttpStatusCode.OK);
        var orderId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetInt32();
        var card = new
        {
            number = "4111111111111111",
            expiry = "12/30",
            key = "rate-card",
        };
        using var paid = await SendAsync(
            client,
            $"/api/orders/{orderId}/pay/card",
            11,
            "buyer",
            card
        );
        await ExpectAsync(paid, HttpStatusCode.OK);
        var before = await SnapshotAsync(app);
        foreach (
            var path in new[]
            {
                $"/api/orders/{orderId}/pay/card",
                $"/api/orders/{orderId}/pay/paypal",
                "/api/orders",
            }
        )
        {
            using var rejected = await SendAsync(client, path, 11, "buyer", card);
            await RejectedAsync(rejected, "payment");
            Check(
                await SnapshotAsync(app) == before,
                "429 cannot change order/payment/stock/ledger/outbox"
            );
        }
        for (var i = 0; i < 2; i++)
        {
            using var payout = await SendAsync(
                client,
                "/api/seller/payouts",
                21,
                "seller",
                new
                {
                    amount = 1,
                    key = "rate-payout-" + i,
                    simulateFailure = false,
                }
            );
            await ExpectAsync(payout, HttpStatusCode.OK);
        }
        before = await SnapshotAsync(app);
        using (var challenge = Request("/api/seller/payouts", 21, "seller"))
        using (var response = await client.SendAsync(challenge))
            await ExpectAsync(response, (HttpStatusCode)428);
        foreach (
            var path in new[] { "/api/seller/payouts", $"/api/seller/orders/{orderId}/refund" }
        )
        {
            using var rejected = await SendAsync(
                client,
                path,
                21,
                "seller",
                new
                {
                    amount = 1,
                    key = "blocked",
                    reason = "cancel",
                }
            );
            await RejectedAsync(rejected, "finance");
            Check(
                await SnapshotAsync(app) == before,
                "429 cannot debit wallet or create payout/refund"
            );
        }
        using var independent = await SendAsync(client, "/probe/read", 11);
        await ExpectAsync(independent, HttpStatusCode.OK);
    }

    private static async Task IdentityAndIpAsync(HttpClient client)
    {
        for (var i = 0; i < 2; i++)
        {
            using var accepted = await SendAsync(client, "/probe/payment", 31);
            await ExpectAsync(accepted, HttpStatusCode.OK);
        }
        using var request = Request("/probe/payment", 31);
        request.Headers.Add("X-User-Id", "999");
        request.Headers.Add("X-Forwarded-For", "203.0.113.31");
        using var sameUser = await client.SendAsync(request);
        await RejectedAsync(sameUser, "payment");
        using var differentUser = await SendAsync(client, "/probe/payment", 32);
        await ExpectAsync(differentUser, HttpStatusCode.OK);
        using var invalid = Request("/probe/payment", 33);
        invalid.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "forged-token");
        using var unauthorized = await client.SendAsync(invalid);
        await ExpectAsync(unauthorized, HttpStatusCode.Unauthorized);

        for (var i = 0; i < 3; i++)
        {
            using var req = Request("/probe/login");
            req.Headers.Add("X-Forwarded-For", "203.0.113.40");
            using var reply = await client.SendAsync(req);
            if (i < 2)
            {
                await ExpectAsync(reply, HttpStatusCode.OK);
                Check(
                    (await reply.Content.ReadAsStringAsync()).Contains("203.0.113.40"),
                    "trusted IP resolved"
                );
            }
            else
                await RejectedAsync(reply, "login");
        }
        using var otherIp = Request("/probe/login");
        otherIp.Headers.Add("X-Forwarded-For", "203.0.113.41");
        using var otherReply = await client.SendAsync(otherIp);
        await ExpectAsync(otherReply, HttpStatusCode.OK);
        for (var i = 0; i < 3; i++)
        {
            using var req = Request("/probe/registration");
            req.Headers.Add("X-Test-Peer", "198.51.100.9");
            req.Headers.Add("X-Forwarded-For", $"203.0.113.{50 + i}");
            using var reply = await client.SendAsync(req);
            if (i < 2)
            {
                await ExpectAsync(reply, HttpStatusCode.OK);
                Check(
                    (await reply.Content.ReadAsStringAsync()).Contains("198.51.100.9"),
                    "untrusted XFF ignored"
                );
            }
            else
                await RejectedAsync(reply, "registration");
        }
        var ipv4 = new DefaultHttpContext();
        ipv4.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        var mapped = new DefaultHttpContext();
        mapped.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:127.0.0.1");
        Check(
            CommerceRateLimiting.Identity(ipv4) == CommerceRateLimiting.Identity(mapped),
            "mapped IP shares quota"
        );
    }

    private static async Task ConcurrentAsync(HttpClient client)
    {
        var replies = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(_ => SendAsync(client, "/probe/write", 77))
        );
        try
        {
            Check(
                replies.Count(x => x.StatusCode == HttpStatusCode.OK) == 2,
                "parallel requests admit exactly quota"
            );
            Check(
                replies.Count(x => x.StatusCode == HttpStatusCode.TooManyRequests) == 10,
                "parallel requests reject excess"
            );
        }
        finally
        {
            foreach (var reply in replies)
                reply.Dispose();
        }
    }

    private static async Task RecoveryAsync()
    {
        await using var app = await StartAsync(readWindow: 1);
        using var client = new HttpClient { BaseAddress = Address(app) };
        for (var i = 0; i < 3; i++)
        {
            using var reply = await SendAsync(client, "/probe/read", 88);
            await ExpectAsync(reply, i < 2 ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests);
        }
        await Task.Delay(1300);
        using var recovered = await SendAsync(client, "/probe/read", 88);
        await ExpectAsync(recovered, HttpStatusCode.OK);
        await app.StopAsync();
    }

    private static Uri Address(WebApplication app) =>
        new(
            app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single()
        );

    private static HttpRequestMessage Request(
        string path,
        int? userId = null,
        string role = "buyer",
        object? body = null
    )
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body ?? new { }),
        };
        if (userId is not null)
        {
            var token = new JwtSecurityToken(
                "rate-limit-tests",
                "rate-limit-tests",
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()!),
                    new Claim(ClaimTypes.Role, role),
                ],
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                    SecurityAlgorithms.HmacSha256
                )
            );
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                new JwtSecurityTokenHandler().WriteToken(token)
            );
        }
        return request;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string path,
        int? userId = null,
        string role = "buyer",
        object? body = null
    )
    {
        using var request = Request(path, userId, role, body);
        if (role == "seller")
            request.Headers.Add(RequirePasswordAttribute.HeaderName, "rate-test-password");
        return await client.SendAsync(request);
    }

    private static async Task RejectedAsync(HttpResponseMessage response, string group)
    {
        await ExpectAsync(response, HttpStatusCode.TooManyRequests);
        Check(response.Headers.RetryAfter?.Delta > TimeSpan.Zero, "positive Retry-After");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Check(
            body.GetProperty("code").GetString() == "RateLimitExceeded"
                && body.GetProperty("group").GetString() == group,
            "typed group response"
        );
    }

    private static async Task<string> SnapshotAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return JsonSerializer.Serialize(
            new
            {
                orders = await db
                    .OrderTables.Select(x => new
                    {
                        x.Id,
                        x.Status,
                        x.TotalPrice,
                    })
                    .ToListAsync(),
                stock = await db.Inventories.Select(x => x.Quantity).ToListAsync(),
                payments = await db.Payments.CountAsync(),
                refunds = await db.Refunds.CountAsync(),
                payouts = await db.SellerPayouts.CountAsync(),
                ledger = await db.FinancialTransactions.CountAsync(),
                outbox = await db.NotificationOutbox.CountAsync(),
                wallets = await db
                    .SellerAccounts.Select(x => new
                    {
                        x.AvailableBalance,
                        x.ProcessingBalance,
                        x.OnHoldBalance,
                        x.NegativeBalance,
                    })
                    .ToListAsync(),
            }
        );
    }

    private static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Check(
            response.StatusCode == expected,
            $"expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception("Rate limit: " + message);
    }

    private sealed class NeverPayPal : IPayPalPaymentService
    {
        public Task<PayPalCreated> StartAsync(
            int orderId,
            string key,
            CancellationToken ct = default
        ) => throw new Exception("Rejected request reached PayPal");

        public Task<Payment> CaptureAsync(
            int orderId,
            string providerOrderId,
            CancellationToken ct = default
        ) => throw new Exception("Rejected request reached PayPal");

        public Task<Payment> ReconcileAsync(int orderId, CancellationToken ct = default) =>
            throw new Exception("Rejected request reached PayPal");
    }
}
