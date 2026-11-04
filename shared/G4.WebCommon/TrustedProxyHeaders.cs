using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace G4.WebCommon;

public static class TrustedProxyHeaders
{
    public static IServiceCollection AddTrustedProxyHeaders(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var proxies = configuration.GetSection("TrustedProxies:Addresses").Get<string[]>() ?? [];
        var hosts = configuration.GetSection("TrustedProxies:Hosts").Get<string[]>() ?? [];
        var addresses = proxies
            .Select(IPAddress.Parse)
            .Concat(hosts.SelectMany(Dns.GetHostAddresses))
            .Distinct()
            .ToArray();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = 1;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var address in addresses)
                options.KnownProxies.Add(address);
        });
        return services;
    }

    public static IApplicationBuilder UseTrustedProxyHeaders(
        this IApplicationBuilder app,
        IConfiguration configuration
    )
    {
        // An empty allowlist must never turn into trust for every remote address.
        if (
            configuration.GetSection("TrustedProxies:Addresses").GetChildren().Any()
            || configuration.GetSection("TrustedProxies:Hosts").GetChildren().Any()
        )
            app.UseForwardedHeaders();
        return app;
    }

    public static void ForwardClientIp(HttpRequestMessage request, HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip is null)
            return;
        // Build a new header from the resolved connection, never copy a browser-supplied chain.
        request.Headers.Remove("X-Forwarded-For");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", ip.ToString());
    }

    public static void CopyRetryAfter(HttpResponseMessage source, HttpResponse destination)
    {
        if (source.Headers.RetryAfter is { } retry)
            destination.Headers.RetryAfter = retry.ToString();
        if ((int)source.StatusCode == StatusCodes.Status429TooManyRequests)
            destination.Headers.CacheControl = "no-store";
    }
}
