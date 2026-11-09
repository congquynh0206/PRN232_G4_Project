using G4.WebCommon;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace frontend.RateLimiting;

public static class LoginRateLimitResponse
{
    public static async Task WriteAsync(HttpContext context, int seconds)
    {
        var state = new ModelStateDictionary();
        state.AddModelError("", CommerceRateLimiting.Message(seconds));
        var view = new ViewResult
        {
            ViewName = "/Views/Account/Login.cshtml",
            StatusCode = StatusCodes.Status429TooManyRequests,
            ViewData = new ViewDataDictionary(
                context.RequestServices.GetRequiredService<IModelMetadataProvider>(),
                state
            ),
        };
        view.ViewData["RetryAfterSeconds"] = seconds;
        await view.ExecuteResultAsync(
            new ActionContext(context, context.GetRouteData(), new ActionDescriptor())
        );
    }
}
