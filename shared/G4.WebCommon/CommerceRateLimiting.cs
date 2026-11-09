using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace G4.WebCommon;

public static class CommerceRateLimiting
{
    public const string Login = "login";
    public const string Registration = "registration";
    public const string Read = "read";
    public const string Write = "write";
    public const string Payment = "payment";
    public const string Finance = "finance";

    public static IServiceCollection AddCommerceRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<HttpContext, int, Task>? loginRejected = null,
        Func<HttpContext, bool>? skipRequest = null
    )
    {
        services.AddRateLimiter(options =>
        {
            AddPolicy(options, configuration, Login, 10, 60, byIp: true);
            AddPolicy(options, configuration, Registration, 5, 600, byIp: true);
            AddPolicy(options, configuration, Read, 120, 60, skipRequest: skipRequest);
            AddPolicy(options, configuration, Write, 60, 60, skipRequest: skipRequest);
            AddPolicy(options, configuration, Payment, 10, 60, skipRequest: skipRequest);
            AddPolicy(options, configuration, Finance, 5, 60, skipRequest: skipRequest);
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (rejected, ct) =>
            {
                var context = rejected.HttpContext;
                var group =
                    context
                        .GetEndpoint()
                        ?.Metadata.GetMetadata<EnableRateLimitingAttribute>()
                        ?.PolicyName
                    ?? Write;
                var fallback = configuration.GetValue(
                    $"RateLimiting:{group}:WindowSeconds",
                    group == Registration ? 600 : 60
                );
                var seconds = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
                    ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds))
                    : fallback;
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = seconds.ToString(
                    CultureInfo.InvariantCulture
                );
                context.Response.Headers.CacheControl = "no-store";
                context
                    .RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("G4.RateLimiting")
                    .LogWarning(
                        "Rate limit rejected group {Group}, retry in {Seconds}s",
                        group,
                        seconds
                    );
                if (group == Login && loginRejected is not null)
                {
                    await loginRejected(context, seconds);
                    return;
                }
                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        error = Message(seconds),
                        code = "RateLimitExceeded",
                        group,
                        retryAfterSeconds = seconds,
                    },
                    ct
                );
            };
        });
        return services;
    }

    public static string Message(int seconds) =>
        $"Bạn thao tác quá nhanh. Vui lòng thử lại sau {seconds} giây.";

    public static string Identity(HttpContext context, bool byIp = false)
    {
        var userId =
            context.User.Identity?.IsAuthenticated == true
                ? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                : null;
        if (!byIp && !string.IsNullOrWhiteSpace(userId))
            return "user:" + userId;
        var ip = context.Connection.RemoteIpAddress;
        if (ip?.IsIPv4MappedToIPv6 == true)
            ip = ip.MapToIPv4();
        return "ip:" + (ip?.ToString() ?? "unknown");
    }

    private static void AddPolicy(
        RateLimiterOptions options,
        IConfiguration configuration,
        string group,
        int permits,
        int windowSeconds,
        bool byIp = false,
        Func<HttpContext, bool>? skipRequest = null
    )
    {
        var section = configuration.GetSection("RateLimiting:" + group);
        permits = section.GetValue("PermitLimit", permits);
        windowSeconds = section.GetValue("WindowSeconds", windowSeconds);
        var segments = section.GetValue("SegmentsPerWindow", 6);
        if (permits < 1 || windowSeconds < 1 || segments < 1 || segments > 60)
            throw new InvalidOperationException($"Invalid rate limit configuration for {group}.");
        options.AddPolicy(
            group,
            context => skipRequest?.Invoke(context) == true
                ? RateLimitPartition.GetNoLimiter(group + ":precondition")
                : RateLimitPartition.GetSlidingWindowLimiter(
                    group + ":" + Identity(context, byIp),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = permits,
                        Window = TimeSpan.FromSeconds(windowSeconds),
                        SegmentsPerWindow = segments,
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }
                )
        );
    }
}
