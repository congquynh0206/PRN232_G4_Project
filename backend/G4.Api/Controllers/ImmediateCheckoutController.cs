using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;

namespace G4.Api.Controllers;

[ApiController]
[Route("api/buyer/immediate-checkouts")]
public sealed class ImmediateCheckoutController(
    ImmediateCheckoutService checkout,
    IHostEnvironment environment
) : ApiControllerBase(environment)
{
    [HttpPost]
    public async Task<IActionResult> Start(ImmediateCheckoutInput input, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await checkout.StartAsync(CurrentUserId, input, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await checkout.GetAsync(CurrentUserId, id, ct));
    }

    [HttpPost("{id:guid}/pay/card")]
    public async Task<IActionResult> Pay(Guid id, CardPaymentRequest input, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        try
        {
            var attempt = await checkout.PayCardAsync(CurrentUserId, id, input, ct);
            return Ok(new { attempt.Status, attempt.Stage, attempt.OrderId, attempt.ErrorCode });
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("stock", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { code = "OutOfStock", error = "Out of Stock" });
        }
    }

    [HttpPost("{id:guid}/reconcile")]
    public async Task<IActionResult> Reconcile(Guid id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        var attempt = await checkout.ReconcileAsync(CurrentUserId, id, ct);
        return Ok(new { attempt.Status, attempt.Stage, attempt.OrderId, attempt.ErrorCode });
    }
}
