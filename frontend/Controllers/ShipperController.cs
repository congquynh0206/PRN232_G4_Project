using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace frontend.Controllers;

[Authorize(Roles = "shipper")]
public sealed class ShipperController : Controller
{
    public IActionResult Index() => View();
}
