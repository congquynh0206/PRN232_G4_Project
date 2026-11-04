using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using G4.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace G4.Api.Security;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequirePasswordAttribute(string role, string action)
    : Attribute, IAsyncAuthorizationFilter
{
    public const string HeaderName = "X-Action-Password";
    public string Role { get; } = role;
    public string Action { get; } = action;

    // This request is guaranteed to stop at the password precondition, before model binding.
    public static bool IsChallenge(HttpContext http) =>
        http.GetEndpoint()?.Metadata.GetMetadata<RequirePasswordAttribute>() != null
        && !http.Request.Headers.ContainsKey(HeaderName);

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        http.Response.Headers.CacheControl = "no-store";
        var currentRole = Role.Split(',').FirstOrDefault(http.User.IsInRole);
        if (
            http.User.Identity?.IsAuthenticated != true
            || currentRole == null
            || !int.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        )
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
            return;
        }

        var header = http.Request.Headers[HeaderName];
        if (header.Count == 0 || string.IsNullOrEmpty(header[0]))
        {
            context.Result = Reply(
                428,
                "PasswordConfirmationRequired",
                "Nhập lại mật khẩu để xác nhận thao tác."
            );
            return;
        }

        using var lease = http.RequestServices
            .GetRequiredService<PasswordAttemptLimiter>()
            .Acquire(userId);
        if (!lease.IsAcquired)
        {
            var seconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
                ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds))
                : 60;
            http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            context.Result = new ObjectResult(new
            {
                code = "RateLimitExceeded",
                group = "password-confirmation",
                retryAfterSeconds = seconds,
                error = $"Bạn đã thử xác nhận quá nhiều lần. Thử lại sau {seconds} giây.",
            })
            {
                StatusCode = 429
            };
            return;
        }

        // URI encoding keeps Unicode passwords and line breaks out of HTTP header syntax.
        var password = header.Count == 1 && header[0]!.Length <= 4096
            ? Uri.UnescapeDataString(header[0]!)
            : "";
        http.Request.Headers.Remove(HeaderName);
        var authentication = http.RequestServices.GetRequiredService<IAuthenticationService>();
        if (
            !await authentication.ValidatePasswordAsync(
                userId,
                currentRole!,
                password,
                http.RequestAborted
            )
        )
            context.Result = Reply(
                403,
                "PasswordConfirmationInvalid",
                "Mật khẩu không đúng. Vui lòng nhập lại."
            );
    }

    private ObjectResult Reply(int status, string code, string error) =>
        new(new
        {
            code,
            error,
            action = Action
        })
        {
            StatusCode = status
        };
}
