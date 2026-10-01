using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace frontend.Controllers;

[Authorize(Roles = "seller")]
public sealed class SellerController : Controller
{
    public IActionResult Index() => View();
}
