using backend.Checkout;
using backend.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Route("api/demo")]
public sealed class DemoController(
    ApplicationDbContext db, MvpService checkout, ShipmentService shipping,
    ReturnService returns, PayPalPaymentService paypal, DemoCarrierState carrierState,
    IHostEnvironment environment) : ControllerBase
{
    public sealed record CardRequest(string Number, string Expiry, string Key);
    public sealed record KeyRequest(string Key);
    public sealed record EventRequest(string Status, string? EventId, string? Location, string? Note);
    public sealed record ReasonRequest(string Reason);
    public sealed record DecisionRequest(bool Approve, string? Reason);
    public sealed record FailRequest(int OrderId, string Direction, int Count);
    public sealed record AddressEditRequest(string FullName, string Street, string City, string State, string Country);

    private bool IsDemo => environment.IsDevelopment();
    private bool Role(string role) => Request.Headers.TryGetValue("X-Demo-Role", out var value) && value == role;

    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap(CancellationToken ct)
    {
        if (!IsDemo) return NotFound();
        await DemoSeed.EnsureAsync(db, ct);
        return Ok(new { message = "Demo data ready" });
    }

    [HttpGet("catalog/random")]
    public async Task<IActionResult> RandomCart([FromQuery] int count = 3, CancellationToken ct = default)
    {
        if (!IsDemo || !Role("buyer")) return StatusCode(403);
        var products = await checkout.RandomProductsAsync(count, ct);
        var ids = products.Select(p => p.Id).ToArray();
        var stock = await db.Inventories.Where(i => ids.Contains(i.ProductId ?? 0)).ToDictionaryAsync(i => i.ProductId!.Value, ct);
        return Ok(products.Select(p => new
        {
            productId = p.Id, title = p.Title, price = p.Price,
            quantity = Random.Shared.Next(1, Math.Min(3, stock[p.Id].Quantity!.Value) + 1),
            available = stock[p.Id].Quantity
        }));
    }

    [HttpGet("addresses")]
    public async Task<IActionResult> Addresses(CancellationToken ct)
    {
        if (!IsDemo || !Role("buyer")) return StatusCode(403);
        var buyerId = await db.Users.Where(u => u.Email == "demo.buyer@example.test").Select(u => u.Id).SingleAsync(ct);
        return Ok(await db.Addresses.Where(a => a.UserId == buyerId)
            .Select(a => new { a.Id, a.FullName, a.Street, a.City, a.State, a.Country, a.IsDefault })
            .ToListAsync(ct));
    }

    [HttpPost("quote")]
    public async Task<IActionResult> Quote(CheckoutRequest request, CancellationToken ct)
    {
        if (!IsDemo || !Role("buyer")) return StatusCode(403);
        return Ok(await checkout.QuoteAsync(request, ct));
    }

    [HttpPost("addresses/{id:int}/edit")]
    public async Task<IActionResult> EditAddress(int id, AddressEditRequest request, CancellationToken ct)
    {
        if (!IsDemo || !Role("buyer")) return StatusCode(403);
        if (new[] { request.FullName, request.Street, request.City, request.State, request.Country }
            .Any(string.IsNullOrWhiteSpace) || request.FullName.Length > 100 || request.Street.Length > 100 ||
            request.City.Length > 50 || request.State.Length > 50 || request.Country.Length > 50)
            return BadRequest(new { error = "Address fields are required and must fit the allowed lengths" });
        var buyerId = await db.Users.Where(u => u.Email == "demo.buyer@example.test")
            .Select(u => u.Id).SingleAsync(ct);
        var address = await db.Addresses.SingleOrDefaultAsync(a => a.Id == id && a.UserId == buyerId, ct);
        if (address is null) return NotFound();
        address.FullName = request.FullName.Trim(); address.Street = request.Street.Trim();
        address.City = request.City.Trim(); address.State = request.State.Trim(); address.Country = request.Country.Trim();
        await db.SaveChangesAsync(ct);
        return Ok(new { address.Id, address.FullName, address.Street, address.City, address.State, address.Country, address.IsDefault });
    }

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder(CheckoutRequest request, CancellationToken ct)
    {
        if (!IsDemo || !Role("buyer")) return StatusCode(403);
        var order = await checkout.CreateOrderAsync(request, ct);
        return Ok(new { order.Id, order.Status, order.TotalPrice, order.PaymentExpiresAt });
    }

    [HttpGet("orders")]
    public async Task<IActionResult> Orders(CancellationToken ct)
    {
        if (!IsDemo) return NotFound();
        if (!Role("buyer") && !Role("seller")) return StatusCode(403);
        return Ok(await db.OrderTables.AsNoTracking().OrderByDescending(o => o.Id)
            .Select(o => new { o.Id, o.OrderDate, o.Status, o.TotalPrice, o.Currency })
            .ToListAsync(ct));
    }

    [HttpGet("orders/{id:int}")]
    public async Task<IActionResult> Order(int id, CancellationToken ct)
    {
        if (!IsDemo) return NotFound();
        if (!Role("buyer") && !Role("seller")) return StatusCode(403);
        var order = await db.OrderTables.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return NotFound();
        var items = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == id)
            .Select(i => new { i.ProductId, i.ProductTitleSnapshot, i.UnitPrice, i.Quantity }).ToListAsync(ct);
        var payments = await db.Payments.AsNoTracking().Where(p => p.OrderId == id)
            .Select(p => new { p.Id, p.Method, p.Status, p.Amount, p.CreatedAt, p.PaidAt, p.ErrorCode }).ToListAsync(ct);
        var shipments = await db.ShippingInfos.AsNoTracking().Where(s => s.OrderId == id)
            .Select(s => new { s.Id, s.Direction, s.TrackingNumber, s.Status, s.DeliveryAttempts, s.FailureReason }).ToListAsync(ct);
        var shipmentIds = shipments.Select(s => s.Id).ToArray();
        var events = await db.ShippingEvents.AsNoTracking().Where(e => shipmentIds.Contains(e.ShippingInfoId))
            .OrderBy(e => e.OccurredAt).Select(e => new { e.ShippingInfoId, e.Status, e.Location, e.Note, e.OccurredAt })
            .ToListAsync(ct);
        var returnRequest = await db.ReturnRequests.AsNoTracking().Where(r => r.OrderId == id)
            .Select(r => new { r.Id, r.Reason, r.Status, r.DecisionReason, r.CreatedAt, r.ReceivedAt }).SingleOrDefaultAsync(ct);
        var refunds = await db.Refunds.AsNoTracking().Where(r => r.OrderId == id)
            .Select(r => new { r.Id, r.Amount, r.Status, r.Reason, r.CreatedAt, r.CompletedAt }).ToListAsync(ct);
        return Ok(new
        {
            order.Id, order.Status, order.OrderDate, order.PaymentExpiresAt, order.AddressSnapshot, order.CancelDecisionReason,
            order.Subtotal, order.DiscountAmount, order.ShippingFee, order.TotalPrice, order.Currency, order.CouponCode,
            items, payments, shipments, events, returnRequest, refunds
        });
    }

    [HttpPost("orders/{id:int}/pay/card")]
    public async Task<IActionResult> PayCard(int id, CardRequest request, CancellationToken ct)
    {
        if (!IsDemo || !Role("buyer")) return StatusCode(403);
        var payment = await checkout.PayCardAsync(id, request.Number, request.Expiry, request.Key, ct);
        return Ok(new { payment.Id, payment.Status, payment.ErrorCode });
    }

    [HttpPost("orders/{id:int}/pay/paypal")]
    public async Task<IActionResult> StartPayPal(int id, KeyRequest request, CancellationToken ct)
    {
        if (!IsDemo || !Role("buyer")) return StatusCode(403);
        return Ok(await paypal.StartAsync(id, request.Key, ct));
    }

    [HttpPost("orders/{id:int}/pay/paypal/{providerId}/capture")]
    public async Task<IActionResult> CapturePayPal(int id, string providerId, CancellationToken ct)
    {
        if (!IsDemo || !Role("buyer")) return StatusCode(403);
        var payment = await paypal.CaptureAsync(id, providerId, ct);
        return Ok(new { payment.Id, payment.Status });
    }

    [HttpPost("orders/{id:int}/pay/paypal/reconcile")]
    public async Task<IActionResult> Reconcile(int id, CancellationToken ct)
    {
        if (!IsDemo || !Role("buyer")) return StatusCode(403);
        var payment = await paypal.ReconcileAsync(id, ct);
        return Ok(new { payment.Id, payment.Status });
    }

    [HttpPost("orders/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        if (!IsDemo || !Role("buyer")) return StatusCode(403);
        var order = await db.OrderTables.SingleAsync(o => o.Id == id, ct);
        var result = order.Status == "AwaitingPayment"
            ? await checkout.CancelUnpaidAsync(id, ct) : await returns.RequestCancelAsync(id, ct);
        return Ok(new { result.Id, result.Status });
    }

    [HttpPost("seller/orders/{id:int}/prepare")]
    public async Task<IActionResult> Prepare(int id, CancellationToken ct)
    {
        if (!IsDemo || !Role("seller")) return StatusCode(403);
        var order = await db.OrderTables.SingleAsync(o => o.Id == id, ct);
        order.Status = OrderState.Next(order.Status!, "Prepare");
        await db.SaveChangesAsync(ct);
        return Ok(new { order.Id, order.Status });
    }

    [HttpPost("seller/orders/{id:int}/ship")]
    public async Task<IActionResult> Ship(int id, CancellationToken ct)
    {
        if (!IsDemo || !Role("seller")) return StatusCode(403);
        var shipment = await shipping.CreateOutboundAsync(id, ct);
        return Ok(new { shipment.Id, shipment.Status, shipment.TrackingNumber });
    }

    [HttpPost("seller/orders/{id:int}/cancel/decision")]
    public async Task<IActionResult> DecideCancel(int id, DecisionRequest request, CancellationToken ct)
    {
        if (!IsDemo || !Role("seller")) return StatusCode(403);
        var order = await returns.DecideCancelAsync(id, request.Approve, request.Reason, ct);
        return Ok(new { order.Id, order.Status });
    }

    [HttpPost("carrier/fail-next")]
    public IActionResult FailNext(FailRequest request)
    {
        if (!IsDemo || !Role("seller")) return StatusCode(403);
        if (request.Direction is not ("Outbound" or "Return")) return BadRequest();
        carrierState.FailNext($"ship-{request.OrderId}-{request.Direction.ToLowerInvariant()}", request.Count);
        return Ok();
    }

    [HttpPost("shipments/{id:int}/events")]
    public async Task<IActionResult> AddEvent(int id, EventRequest request, CancellationToken ct)
    {
        if (!IsDemo || !Role("seller")) return StatusCode(403);
        var applied = await shipping.RecordEventAsync(id, request.EventId ?? Guid.NewGuid().ToString("N"),
            request.Status, request.Location, request.Note, ct);
        return Ok(new { applied });
    }

    [HttpPost("orders/{id:int}/returns")]
    public async Task<IActionResult> RequestReturn(int id, ReasonRequest request, CancellationToken ct)
    {
        if (!IsDemo || !Role("buyer")) return StatusCode(403);
        var result = await returns.RequestReturnAsync(id, request.Reason, ct);
        return Ok(new { result.Id, result.Status });
    }

    [HttpPost("seller/returns/{id:int}/approve")]
    public async Task<IActionResult> ApproveReturn(int id, CancellationToken ct)
    {
        if (!IsDemo || !Role("seller")) return StatusCode(403);
        var result = await returns.ApproveAsync(id, ct);
        return Ok(new { result.Id, result.Status });
    }

    [HttpPost("seller/returns/{id:int}/reject")]
    public async Task<IActionResult> RejectReturn(int id, ReasonRequest request, CancellationToken ct)
    {
        if (!IsDemo || !Role("seller")) return StatusCode(403);
        var result = await returns.RejectAsync(id, request.Reason, ct);
        return Ok(new { result.Id, result.Status });
    }

    [HttpPost("seller/returns/{id:int}/receive")]
    public async Task<IActionResult> ReceiveReturn(int id, CancellationToken ct)
    {
        if (!IsDemo || !Role("seller")) return StatusCode(403);
        var result = await returns.MarkReceivedAsync(id, ct);
        return Ok(new { result.Id, result.Status });
    }

    [HttpPost("seller/orders/{id:int}/refund")]
    public async Task<IActionResult> Refund(int id, ReasonRequest request, CancellationToken ct)
    {
        if (!IsDemo || !Role("seller")) return StatusCode(403);
        var returnId = request.Reason == "return"
            ? await db.ReturnRequests.Where(r => r.OrderId == id).Select(r => (int?)r.Id).SingleAsync(ct) : null;
        var result = await returns.RefundAsync(id, request.Reason, returnId, ct);
        return Ok(new { result.Id, result.Status, result.Amount });
    }

    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox(CancellationToken ct)
    {
        if (!IsDemo) return NotFound();
        if (!Role("buyer") && !Role("seller")) return StatusCode(403);
        return Ok(await db.NotificationOutbox.AsNoTracking().OrderByDescending(n => n.Id)
            .Select(n => new { n.Id, n.OrderId, n.EventType, n.Recipient, n.Subject, n.Body, n.Status, n.CreatedAt })
            .ToListAsync(ct));
    }
}
