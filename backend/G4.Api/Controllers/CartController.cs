using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;

namespace G4.Api.Controllers;
[ApiController]
[Route("api/cart")]
public sealed class CartController(IBuyerCartService carts, IHostEnvironment environment) : ApiControllerBase(environment)
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await carts.GetAsync(CurrentUserId, ct));
    }

    [HttpPost("save")]
    public async Task<IActionResult> Save(SaveCartRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await carts.SaveAsync(CurrentUserId, request, ct));
    }

    [HttpPost("random")]
    public async Task<IActionResult> Random(RandomCartRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await carts.RandomAsync(CurrentUserId, request, ct));
    }

    [HttpPost("quote")]
    public async Task<IActionResult> Quote(CartCheckoutRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await carts.QuoteAsync(CurrentUserId, request, ct));
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(CartCheckoutRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await carts.CheckoutAsync(CurrentUserId, request, ct));
    }
}
