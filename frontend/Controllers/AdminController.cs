using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace frontend.Controllers;

[Authorize(Roles = "admin")]
public sealed class AdminController : Controller
{
    public IActionResult Index() => View();
}
