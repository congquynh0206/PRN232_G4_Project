using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace frontend.Controllers;
[Authorize]
public sealed class RealtimeController(IConfiguration configuration) : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Session()
    {
        var token = User.FindFirstValue("access_token");
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized();
        var origin = configuration["Realtime:PublicApiBaseUrl"] ?? configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5251";
        return Json(new { url = origin.TrimEnd('/') + "/hubs/commerce", token });
    }
}
