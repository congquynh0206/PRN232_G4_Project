using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace frontend.Controllers;

[Authorize]
public sealed class CheckoutController : Controller
{
    public IActionResult Index() => User.FindFirstValue(ClaimTypes.Role) switch
    {
        "buyer" => RedirectToAction("Index", "Buyer"),
        "seller" => RedirectToAction("Index", "Seller"),
        "shipper" => RedirectToAction("Index", "Shipper"),
        "admin" => RedirectToAction("Index", "Admin"),
        _ => RedirectToAction("AccessDenied", "Account")
    };
}
