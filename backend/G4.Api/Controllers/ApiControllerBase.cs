using Microsoft.AspNetCore.Mvc;

namespace G4.Api.Controllers;

public abstract class ApiControllerBase(IHostEnvironment environment) : ControllerBase
{
    protected bool IsAvailable => environment.IsDevelopment();

    protected bool HasRole(string role) =>
        Request.Headers.TryGetValue("X-Actor-Role", out var value) && value == role;

    protected bool HasAnyRole() => HasRole("buyer") || HasRole("seller");
}
