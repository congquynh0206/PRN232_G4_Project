using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace G4.Api.Controllers;

public abstract class ApiControllerBase(IHostEnvironment environment) : ControllerBase
{
    protected bool IsAvailable => environment.IsDevelopment();

    protected int CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new UnauthorizedAccessException("Missing user identity");

    protected bool HasRole(string role) => User.IsInRole(role);

    protected bool HasAnyRole() => HasRole("buyer") || HasRole("seller") || HasRole("admin") || HasRole("shipper");
}
