

using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class PaymentsController(ApplicationDbContext db, ICheckoutService checkout, IReturnService returns,
    IPayPalPaymentService paypal, IHostEnvironment environment) : ApiControllerBase(environment)
{
    [HttpPost("orders/{id:int}/pay/card")]
    public async Task<IActionResult> PayCard(int id, CardPaymentRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer")) return StatusCode(403);
        var payment = await checkout.PayCardAsync(id, request.Number, request.Expiry, request.Key, ct);
        return Ok(new { payment.Id, payment.Status, payment.ErrorCode });
    }

    [HttpPost("orders/{id:int}/pay/paypal")]
    public async Task<IActionResult> StartPayPal(int id, IdempotencyKeyRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer")) return StatusCode(403);
        return Ok(await paypal.StartAsync(id, request.Key, ct));
    }

    [HttpPost("orders/{id:int}/pay/paypal/{providerId}/capture")]
    public async Task<IActionResult> CapturePayPal(int id, string providerId, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer")) return StatusCode(403);
        var payment = await paypal.CaptureAsync(id, providerId, ct);
        return Ok(new { payment.Id, payment.Status });
    }

    [HttpPost("orders/{id:int}/pay/paypal/reconcile")]
    public async Task<IActionResult> Reconcile(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer")) return StatusCode(403);
        var payment = await paypal.ReconcileAsync(id, ct);
        return Ok(new { payment.Id, payment.Status });
    }

    [HttpPost("seller/orders/{id:int}/refund")]
    public async Task<IActionResult> Refund(int id, ReasonRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        var returnId = request.Reason == "return"
            ? await db.ReturnRequests.Where(r => r.OrderId == id).Select(r => (int?)r.Id).SingleAsync(ct) : null;
        var result = await returns.RefundAsync(id, request.Reason, returnId, ct);
        return Ok(new { result.Id, result.Status, result.Amount });
    }
}
