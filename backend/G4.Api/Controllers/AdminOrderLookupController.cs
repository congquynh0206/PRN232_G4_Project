using G4.Application.Services;
using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;

namespace G4.Api.Controllers;

[ApiController]
[Route("api/admin/order-lookup")]
public sealed class AdminOrderLookupController(
    IAdminOrderLookupService orders,
    IHostEnvironment environment
) : ApiControllerBase(environment)
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] AdminOrderLookupQuery query,
        CancellationToken ct
    )
    {
        if (!IsAvailable || !HasRole("admin"))
            return StatusCode(403);
        return Ok(await orders.SearchAsync(query, ct));
    }

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> Detail(int orderId, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("admin"))
            return StatusCode(403);
        return Ok(await orders.DetailAsync(orderId, ct));
    }
}
