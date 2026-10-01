using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace frontend.Controllers;

[Authorize]
public sealed class ApiProxyController(IHttpClientFactory factory, IConfiguration config) : Controller
{
    [AcceptVerbs("GET", "POST")]
    [Route("api/proxy/{**path}")]
    public async Task<IActionResult> Api(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal)) return BadRequest();
        using var request = new HttpRequestMessage(new HttpMethod(Request.Method), BackendUrl("api/" + path + Request.QueryString));
        var token = User.FindFirstValue("access_token");
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (Request.Method == "POST")
        {
            request.Content = new StreamContent(Request.Body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        using var response = await factory.CreateClient().SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return new ContentResult
        {
            Content = body,
            ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
            StatusCode = (int)response.StatusCode
        };
    }

    private string BackendUrl(string path) =>
        (config["ApiSettings:BaseUrl"] ?? "http://localhost:5251").TrimEnd('/') + "/" + path;
}
