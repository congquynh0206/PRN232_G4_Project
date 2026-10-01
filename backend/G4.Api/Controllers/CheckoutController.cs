
using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;

namespace G4.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class CheckoutController(ICheckoutService checkout, IHostEnvironment environment) : ApiControllerBase(environment)
{
    [HttpPost("quote")]
    public async Task<IActionResult> Quote(CheckoutRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer")) return StatusCode(403);
        return Ok(await checkout.QuoteAsync(CurrentUserId, request, ct));
    }

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder(CheckoutRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer")) return StatusCode(403);
        var order = await checkout.CreateOrderAsync(CurrentUserId, request, ct);
        return Ok(new { order.Id, order.Status, order.TotalPrice, order.PaymentExpiresAt });
    }
}
