using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;
namespace G4.Api.Controllers;
[ApiController]
[Route("api/buyer/paypal-checkouts")]
public sealed class PayPalCartController(IPayPalCartPaymentService payments, IHostEnvironment environment) : ApiControllerBase(environment)
{
    [HttpPost]
    public async Task<IActionResult> Start(PayPalCartStartInput input, CancellationToken ct)
    { if (!IsAvailable || !HasRole("buyer")) return StatusCode(403); return Ok(await payments.StartAsync(CurrentUserId, input, ct)); }
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    { if (!IsAvailable || !HasRole("buyer")) return StatusCode(403); return Ok(await payments.GetAsync(CurrentUserId, id, ct)); }
    [HttpPost("{id:long}/capture")]
    public async Task<IActionResult> Capture(long id, PayPalCartCaptureInput input, CancellationToken ct)
    { if (!IsAvailable || !HasRole("buyer")) return StatusCode(403); return Ok(await payments.CaptureAsync(CurrentUserId, id, input.ProviderOrderId, ct)); }
    [HttpPost("{id:long}/reconcile")]
    public async Task<IActionResult> Reconcile(long id, CancellationToken ct)
    { if (!IsAvailable || !HasRole("buyer")) return StatusCode(403); return Ok(await payments.ReconcileAsync(CurrentUserId, id, ct)); }
    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
    { if (!IsAvailable || !HasRole("buyer")) return StatusCode(403); return Ok(await payments.CancelAsync(CurrentUserId, id, ct)); }
}
