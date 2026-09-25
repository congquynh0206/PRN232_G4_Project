using backend.Checkout;
using backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{name}: expected {expected}, got {actual}");
}

var quote = OrderPricing.Calculate(
    new[] { new PriceLine(10m, 2), new PriceLine(5m, 1) },
    10m, sameState: false);
Equal(25m, quote.Subtotal, "subtotal");
Equal(2.50m, quote.Discount, "discount");
Equal(5m, quote.Shipping, "shipping");
Equal(27.50m, quote.Total, "total");

try
{
    OrderPricing.Calculate(new[] { new PriceLine(10m, 0) }, 0m, true);
    throw new Exception("zero quantity accepted");
}
catch (ArgumentOutOfRangeException) { }

Equal("Succeeded", FakeCardProcessor.Outcome("4111111111111111", "12/30"), "valid card");
Equal("Declined", FakeCardProcessor.Outcome("4000000000000002", "12/30"), "declined card");
Equal("Expired", FakeCardProcessor.Outcome("4111111111111111", "01/20"), "expired card");

Equal("Paid", OrderState.Next("AwaitingPayment", "PaymentSucceeded"), "payment success");
Equal("Delivered", ShippingState.Next("OutForDelivery", "Delivered"), "delivery");
if (ShippingState.TryNext("Delivered", "InTransit", out _))
    throw new Exception("late shipment event rolled back status");

Console.WriteLine("8 domain checks passed");

var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
await using var db = new ApplicationDbContext(options);
var buyer = new User { Id = 11, Username = "demo_buyer", Role = "buyer", Email = "demo.buyer@example.test" };
var seller = new User { Id = 22, Username = "demo_seller", Role = "seller", Email = "demo.seller@example.test" };
var product = new Product { Id = 10, Title = "Camera", Price = 10m, SellerId = 22, IsAuction = false };
var inventory = new Inventory { Id = 10, ProductId = 10, Quantity = 3 };
var address = new Address { Id = 11, UserId = 11, FullName = "Buyer", State = "Hanoi", City = "Hanoi", Country = "Vietnam", Street = "1 Demo St" };
db.Users.AddRange(buyer, seller);
db.Products.Add(product);
db.Inventories.Add(inventory);
db.Addresses.Add(address);
await db.SaveChangesAsync();

var service = new MvpService(db);
var request = new CheckoutRequest(11, new[] { new CartLine(10, 2) }, null, "checkout-one");
var first = await service.CreateOrderAsync(request);
var again = await service.CreateOrderAsync(request);
Equal(first.Id, again.Id, "duplicate checkout ID");
try
{
    await service.CreateOrderAsync(request with { Items = new[] { new CartLine(10, 1) } });
    throw new Exception("checkout key accepted different cart");
}
catch (InvalidOperationException) { }
Equal(1, inventory.Quantity, "inventory reserved once");
Equal(22m, first.TotalPrice, "server computes total");

var card = await service.PayCardAsync(first.Id, "4111111111111111", "12/30", "pay-one");
Equal("Succeeded", card.Status, "card payment");
var sameCard = await service.PayCardAsync(first.Id, "4111111111111111", "12/30", "pay-one");
Equal(card.Id, sameCard.Id, "duplicate card payment");
Equal("Paid", first.Status, "order paid");

Console.WriteLine("5 checkout checks passed");

var carrier = new TestCarrier();
var shipmentService = new ShipmentService(db, carrier);
var shipment = await shipmentService.CreateOutboundAsync(first.Id);
Equal(3, carrier.Attempts, "carrier retries twice before success");
var sameShipment = await shipmentService.CreateOutboundAsync(first.Id);
Equal(shipment.Id, sameShipment.Id, "single outbound label");
await shipmentService.RecordEventAsync(shipment.Id, "event-1", "PickedUp");
await shipmentService.RecordEventAsync(shipment.Id, "event-2", "InTransit");
await shipmentService.RecordEventAsync(shipment.Id, "event-3", "OutForDelivery");
await shipmentService.RecordEventAsync(shipment.Id, "event-4", "Delivered");
await shipmentService.RecordEventAsync(shipment.Id, "event-4", "Delivered");
Equal(5, await db.ShippingEvents.CountAsync(), "duplicate event ignored including label");
Equal("Delivered", first.Status, "delivered order");
if (await shipmentService.RecordEventAsync(shipment.Id, "late", "InTransit"))
    throw new Exception("late event accepted");

var refundGateway = new TestRefundGateway();
var returns = new ReturnService(db, refundGateway, carrier);
var requestReturn = await returns.RequestReturnAsync(first.Id, "Not as described");
await returns.ApproveAsync(requestReturn.Id);
try
{
    await returns.MarkReceivedAsync(requestReturn.Id);
    throw new Exception("seller received return before tracking delivery");
}
catch (InvalidOperationException) { }
var returnShipment = await db.ShippingInfos.SingleAsync(s => s.OrderId == first.Id && s.Direction == "Return");
foreach (var (eventId, status) in new[] { ("return-1", "PickedUp"), ("return-2", "InTransit"),
    ("return-3", "OutForDelivery"), ("return-4", "Delivered") })
    await shipmentService.RecordEventAsync(returnShipment.Id, eventId, status);
Equal("ReturnShipping", requestReturn.Status, "tracking does not confirm seller receipt");
await returns.MarkReceivedAsync(requestReturn.Id);
var refund = await returns.RefundAsync(first.Id, "return", requestReturn.Id);
Equal("Succeeded", refund.Status, "refund result");
Equal(refund.Id, requestReturn.RefundId, "return references saved refund");
Equal("Closed", first.Status, "refunded return closes order");
var sameRefund = await returns.RefundAsync(first.Id, "return", requestReturn.Id);
Equal(refund.Id, sameRefund.Id, "single refund");
Equal(1, refundGateway.Calls, "provider called once");

var oldPurchase = new OrderTable { Id = 101, BuyerId = buyer.Id, SellerId = seller.Id, Status = "Delivered",
    OrderDate = DateTime.UtcNow.AddDays(-10), TotalPrice = 12m, Currency = "USD" };
var oldDelivery = new OrderTable { Id = 102, BuyerId = buyer.Id, SellerId = seller.Id, Status = "Delivered",
    OrderDate = DateTime.UtcNow.AddDays(-11), TotalPrice = 12m, Currency = "USD" };
db.OrderTables.AddRange(oldPurchase, oldDelivery);
db.ShippingInfos.AddRange(
    new ShippingInfo { OrderId = oldPurchase.Id, Direction = "Outbound", Status = "Delivered", DeliveredAt = DateTime.UtcNow },
    new ShippingInfo { OrderId = oldDelivery.Id, Direction = "Outbound", Status = "Delivered", DeliveredAt = DateTime.UtcNow.AddDays(-8) });
await db.SaveChangesAsync();
await returns.RequestReturnAsync(oldPurchase.Id, "Delivered recently");
try
{
    await returns.RequestReturnAsync(oldDelivery.Id, "Delivered too long ago");
    throw new Exception("return accepted beyond delivery window");
}
catch (InvalidOperationException) { }
Equal(1, await service.CloseDeliveredOutsideReturnWindowAsync(), "close one delivered order after return window");
Equal("Closed", oldDelivery.Status, "old delivered order closed");

Console.WriteLine("Shipping and return checks passed");

var seedOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
await using var seedDb = new ApplicationDbContext(seedOptions);
await DemoSeed.EnsureAsync(seedDb);
await DemoSeed.EnsureAsync(seedDb);
Equal(2, await seedDb.Users.CountAsync(), "seed users idempotent");
Equal(5, await seedDb.Products.CountAsync(), "seed products idempotent");
Equal(2, await seedDb.Addresses.CountAsync(), "seed addresses idempotent");
Console.WriteLine("3 seed checks passed");

var demoProduct = await seedDb.Products.FirstAsync();
var demoAddress = await seedDb.Addresses.FirstAsync(a => a.User!.Email == "demo.buyer@example.test");
var demoStock = await seedDb.Inventories.SingleAsync(i => i.ProductId == demoProduct.Id);
var initialStock = demoStock.Quantity;
var demoCheckout = new MvpService(seedDb);
var expiring = await demoCheckout.CreateOrderAsync(new CheckoutRequest(demoAddress.Id,
    new[] { new CartLine(demoProduct.Id, 1) }, null, "expire-test"));
expiring.PaymentExpiresAt = DateTime.UtcNow.AddSeconds(-1);
await seedDb.SaveChangesAsync();
Equal(1, await demoCheckout.ExpirePendingAsync(), "one expired order");
Equal("Expired", expiring.Status, "expired status");
Equal(initialStock, demoStock.Quantity, "expired stock restored");
try
{
    await demoCheckout.PayCardAsync(expiring.Id, "4111111111111111", "12/30", "late-card");
    throw new Exception("expired order accepted payment");
}
catch (InvalidOperationException) { }

var cardOrder = await demoCheckout.CreateOrderAsync(new CheckoutRequest(demoAddress.Id,
    new[] { new CartLine(demoProduct.Id, 1) }, null, "card-retry"));
Equal("Declined", (await demoCheckout.PayCardAsync(cardOrder.Id, "4000000000000002", "12/30", "declined-card")).Status,
    "declined card attempt");
Equal("AwaitingPayment", cardOrder.Status, "failed card leaves order payable");
Equal("Succeeded", (await demoCheckout.PayCardAsync(cardOrder.Id, "4111111111111111", "12/30", "good-card")).Status,
    "card retry succeeds");
var cancelService = new ReturnService(seedDb, new TestRefundGateway(), new TestCarrier());
await cancelService.RequestCancelAsync(cardOrder.Id);
try
{
    await cancelService.DecideCancelAsync(cardOrder.Id, false);
    throw new Exception("seller rejected cancel without reason");
}
catch (ArgumentException) { }
await cancelService.DecideCancelAsync(cardOrder.Id, false, "Already packed");
Equal("Paid", cardOrder.Status, "rejected cancel restores prior status");
Equal("Already packed", cardOrder.CancelDecisionReason, "cancel rejection reason saved");

var paypalOrder = await demoCheckout.CreateOrderAsync(new CheckoutRequest(demoAddress.Id,
    new[] { new CartLine(demoProduct.Id, 1) }, null, "paypal-test"));
var paypalGateway = new TestPayPalGateway();
var paypalService = new PayPalPaymentService(seedDb, paypalGateway, new ConfigurationBuilder().Build());
var paypalStart = await paypalService.StartAsync(paypalOrder.Id, "paypal-key");
Equal("PAYPAL-ORDER", paypalStart.Id, "PayPal create");
paypalGateway.TimeoutCapture = true;
Equal("Verifying", (await paypalService.CaptureAsync(paypalOrder.Id, paypalStart.Id)).Status, "PayPal timeout verifying");
Equal("AwaitingPayment", paypalOrder.Status, "timeout does not mark paid");
paypalOrder.PaymentExpiresAt = DateTime.UtcNow.AddSeconds(-1);
await seedDb.SaveChangesAsync();
Equal(0, await demoCheckout.ExpirePendingAsync(), "uncertain PayPal capture waits for reconciliation");
paypalGateway.TimeoutCapture = false;
var reconciled = await paypalService.ReconcileAsync(paypalOrder.Id);
Equal("Succeeded", reconciled.Status, "PayPal reconcile success");
Equal("Paid", paypalOrder.Status, "PayPal order paid after reconciliation");
Equal(1, await seedDb.Payments.CountAsync(p => p.OrderId == paypalOrder.Id && p.Status == "Succeeded"),
    "one PayPal capture");
var paypalHttp = new TestPayPalHttpHandler();
var paypalConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["PayPal:ClientId"] = "sandbox-client", ["PayPal:ClientSecret"] = "sandbox-secret"
}).Build();
var restGateway = new PayPalSandboxGateway(new HttpClient(paypalHttp), paypalConfig);
Equal("REST-ORDER", (await restGateway.CreateAsync(12.34m, "USD", "create-key", "http://localhost/return", "http://localhost/cancel", default)).Id,
    "PayPal REST create response");
Equal(12.34m, (await restGateway.CaptureAsync("REST-ORDER", "capture-key", default)).Amount,
    "PayPal REST capture amount");
Equal("REST-CAPTURE", (await restGateway.GetAsync("REST-ORDER", default)).CaptureId,
    "PayPal REST lookup capture");
Equal("REST-REFUND", await restGateway.RefundAsync("REST-CAPTURE", 12.34m, "USD", "refund-key", default),
    "PayPal REST refund response");
Equal(4, paypalHttp.AuthRequests, "PayPal authenticates REST operations");
Console.WriteLine("Expiry, card, and PayPal checks passed");

sealed class TestCarrier : ICarrierGateway
{
    public int Attempts { get; private set; }
    public Task<string> CreateLabelAsync(int orderId, string direction, string key, CancellationToken ct)
    {
        Attempts++;
        if (Attempts < 3) throw new HttpRequestException("temporary carrier failure");
        return Task.FromResult("TRACK-" + orderId + "-" + direction);
    }
}

sealed class TestRefundGateway : IRefundGateway
{
    public int Calls { get; private set; }
    public Task<string> RefundAsync(Payment payment, decimal amount, string key, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult("REFUND-" + payment.Id);
    }
}

sealed class TestPayPalGateway : IPayPalGateway
{
    public bool TimeoutCapture { get; set; }
    private decimal _amount;
    public Task<PayPalCreated> CreateAsync(decimal amount, string currency, string requestId,
        string returnUrl, string cancelUrl, CancellationToken ct)
    {
        _amount = amount;
        return Task.FromResult(new PayPalCreated("PAYPAL-ORDER", "https://example.test/approve"));
    }
    public Task<PayPalCaptured> CaptureAsync(string orderId, string requestId, CancellationToken ct) =>
        TimeoutCapture ? throw new TaskCanceledException("timeout") : GetAsync(orderId, ct);
    public Task<PayPalCaptured> GetAsync(string orderId, CancellationToken ct) =>
        Task.FromResult(new PayPalCaptured("COMPLETED", "PAYPAL-CAPTURE", _amount, "USD"));
    public Task<string> RefundAsync(string captureId, decimal amount, string currency, string requestId,
        CancellationToken ct) => Task.FromResult("PAYPAL-REFUND");
}

sealed class TestPayPalHttpHandler : HttpMessageHandler
{
    public int AuthRequests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string json;
        var path = request.RequestUri?.AbsolutePath ?? "";
        if (path == "/v1/oauth2/token")
        {
            AuthRequests++;
            json = "{\"access_token\":\"test-token\"}";
        }
        else
        {
            if (request.Headers.Authorization?.Scheme != "Bearer") throw new Exception("PayPal request lacks Bearer token");
            json = path switch
            {
                "/v2/checkout/orders" => "{\"id\":\"REST-ORDER\",\"links\":[{\"rel\":\"approve\",\"href\":\"https://example.test/approve\"}]}",
                "/v2/checkout/orders/REST-ORDER/capture" or "/v2/checkout/orders/REST-ORDER" =>
                    "{\"status\":\"COMPLETED\",\"purchase_units\":[{\"payments\":{\"captures\":[{\"id\":\"REST-CAPTURE\",\"amount\":{\"value\":\"12.34\",\"currency_code\":\"USD\"}}]}}]}",
                "/v2/payments/captures/REST-CAPTURE/refund" => "{\"id\":\"REST-REFUND\",\"status\":\"COMPLETED\"}",
                _ => throw new Exception("Unexpected PayPal URL: " + path)
            };
        }
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
