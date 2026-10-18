using G4.Application.Integrations;
using G4.Domain.Entities;
using G4.Infrastructure.Checkout;
using G4.Infrastructure.Diagnostics;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Notifications;
using G4.Infrastructure.Payments;
using G4.Infrastructure.Persistence;
using G4.Infrastructure.Returns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class BusinessNotificationChecks
{
    internal static void Check(bool ok, string message) { if (!ok) throw new Exception("Business notifications: " + message); }
    internal static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["Frontend:PublicBaseUrl"] = "http://localhost:5131", ["Mail:Mode"] = "Smtp", ["Finance:WriteOff:Enabled"] = "false"
    }).Build();
    internal sealed record Fixture(int Buyer, int Seller, int OtherBuyer, int OtherSeller, int Product);
    internal static async Task<Fixture> Seed(ApplicationDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var users = new[] { new User { Role = "buyer", Username = "N1 Buyer", Email = $"n1-buyer-{suffix}@example.test" },
            new User { Role = "seller", Username = "N1 Seller", Email = $"n1-seller-{suffix}@example.test" },
            new User { Role = "buyer", Username = "Other buyer", Email = $"n1-other-buyer-{suffix}@example.test" },
            new User { Role = "seller", Username = "Other seller", Email = $"n1-other-seller-{suffix}@example.test" } };
        db.Users.AddRange(users); await db.SaveChangesAsync();
        var product = new Product { SellerId = users[1].Id, Title = "N1 Headphones", Price = 50, WeightKg = 1.2m, IsAuction = false };
        db.Products.Add(product); await db.SaveChangesAsync(); db.Inventories.Add(new Inventory { ProductId = product.Id, Quantity = 100 });
        db.SellerAccounts.Add(new SellerAccount { SellerId = users[1].Id, Level = 3, Status = "Active", MonthlySalesLimit = 1000000,
            AvailableBalance = 500, SalesMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1), UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(); return new(users[0].Id, users[1].Id, users[2].Id, users[3].Id, product.Id);
    }
    internal static async Task<OrderTable> Order(ApplicationDbContext db, Fixture f, string status = "AwaitingPayment")
    {
        var order = new OrderTable { BuyerId = f.Buyer, SellerId = f.Seller, Status = status, TotalPrice = 52.10m, Subtotal = 50,
            ShippingFee = 2.10m, TotalWeightKg = 1.2m, AddressSnapshot = "Buyer, Street, Hanoi, Hanoi, Vietnam",
            PickupAddressSnapshot = "Seller, Street, Hanoi, Hanoi, Vietnam", OrderDate = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            PaymentExpiresAt = DateTime.UtcNow.AddMinutes(15) };
        db.OrderTables.Add(order); await db.SaveChangesAsync();
        db.OrderItems.Add(new OrderItem { OrderId = order.Id, ProductId = f.Product, UnitPrice = 50, Quantity = 1, ProductTitleSnapshot = "N1 Headphones" });
        if (status != "AwaitingPayment") db.Payments.Add(new Payment { OrderId = order.Id, UserId = f.Buyer, Method = "Card", Amount = order.TotalPrice,
            Status = "Succeeded", CreatedAt = DateTime.UtcNow, PaidAt = DateTime.UtcNow, IdempotencyKey = Guid.NewGuid().ToString("N") });
        if (status == "Delivered") db.ShippingInfos.Add(new ShippingInfo { OrderId = order.Id, Direction = "Outbound", Status = "Delivered",
            TrackingNumber = "N1-" + order.Id, DeliveredAt = DateTime.UtcNow });
        await db.SaveChangesAsync(); return order;
    }
    public static async Task RunAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("n1-" + Guid.NewGuid()).Options;
        await using var db = new ApplicationDbContext(options); var f = await Seed(db); var config = Config();
        var clock = new Clock(); var finance = new SellerFinanceService(db, config); var checkout = new CheckoutService(db, config: config);
        var returns = new ReturnService(db, new ReturnAutomationChecks.Gateway(), new ReturnAutomationChecks.Carrier(), finance, config);
        var unpaid = await Order(db, f);
        var fail1 = await checkout.PayCardAsync(unpaid.Id, "4000000000000002", "12/30", "decline-a");
        await checkout.PayCardAsync(unpaid.Id, "4000000000000002", "12/30", "decline-a");
        await checkout.PayCardAsync(unpaid.Id, "4111111111111111", "01/20", "expired-b");
        Check(await db.NotificationOutbox.CountAsync(n => n.OrderId == unpaid.Id && n.EventType == "PaymentFailed") == 2, "each failed attempt gets one mail, retry same key stays one");
        await checkout.CancelUnpaidAsync(unpaid.Id); await checkout.CancelUnpaidAsync(unpaid.Id);
        Check(await db.NotificationOutbox.CountAsync(n => n.OrderId == unpaid.Id && n.EventType == "OrderCancelled") == 1, "unpaid cancellation acknowledged once");
        var paid = await Order(db, f, "Paid");
        await returns.RequestCancelAsync(paid.Id); await returns.RequestCancelAsync(paid.Id);
        Check(await db.NotificationOutbox.CountAsync(n => n.OrderId == paid.Id && n.EventType == "CancellationRequested") == 2, "paid cancellation goes once to buyer and seller");
        await returns.DecideCancelAsync(paid.Id, false, "Đang đóng gói"); await returns.RequestCancelAsync(paid.Id);
        Check(await db.NotificationOutbox.CountAsync(n => n.OrderId == paid.Id && n.EventType == "CancellationRequested") == 4 && paid.CancelRequestNumber == 2, "new cancellation cycle gets separate notifications");
        var delivered = await Order(db, f, "Delivered"); var quote = await returns.QuoteAsync(delivered.Id, "WrongItem");
        var request = await returns.RequestPricedReturnAsync(delivered.Id, new("WrongItem", "<script>reason</script>", "return-a", quote.PricingFingerprint));
        await returns.RequestPricedReturnAsync(delivered.Id, new("WrongItem", "<script>reason</script>", "return-a", quote.PricingFingerprint));
        Check(await db.NotificationOutbox.CountAsync(n => n.OrderId == delivered.Id && n.EventType == "ReturnRequested") == 2, "priced request and retry enqueue once per party");
        await returns.ApproveAsync(request.Id); await returns.ApproveAsync(request.Id);
        Check(await db.NotificationOutbox.CountAsync(n => n.OrderId == delivered.Id && n.EventType == "ReturnApproved") == 1, "approval only notifies buyer once");
        var rejected = await Order(db, f, "Delivered"); var legacy = await returns.RequestReturnAsync(rejected.Id, "Wrong item");
        await returns.RejectAsync(legacy.Id, "<script>decision</script>");
        var rejection = await db.NotificationOutbox.SingleAsync(n => n.OrderId == rejected.Id && n.EventType == "ReturnRejected");
        Check(rejection.Body.Contains("<script>decision</script>") && rejection.HtmlBody!.Contains("&lt;script&gt;") && !rejection.HtmlBody.Contains("<script>"), "rejection explains reason as encoded text");
        var instant = await finance.RequestPayoutAsync(f.Seller, 10, "instant-payout", false);
        await finance.RequestPayoutAsync(f.Seller, 10, "instant-payout", false);
        Check(await db.NotificationOutbox.CountAsync(n => n.PayoutId == instant.Id && n.EventType == "PayoutCompleted") == 1, "instant completed payout immediately notifies once");
        var returned = await finance.RequestPayoutAsync(f.Seller, 12, "returned-payout", true);
        Check(!await db.NotificationOutbox.AnyAsync(n => n.PayoutId == returned.Id), "pending payout cannot claim completion");
        for (var i = 0; i < 4; i++) await finance.AdvancePayoutsAsync();
        Check(await db.NotificationOutbox.CountAsync(n => n.PayoutId == returned.Id && n.EventType == "PayoutReturned") == 1, "returned payout notifies once after funds restored");
        var diagnostics = new DiagnosticsQueryService(db);
        var buyerMails = await diagnostics.NotificationsAsync(f.Buyer, "buyer", new(PageSize: 100), default);
        var sellerMails = await diagnostics.NotificationsAsync(f.Seller, "seller", new(PageSize: 100), default);
        Check(buyerMails.Items.All(n => n.PayoutId == null) && sellerMails.Items.All(n => n.EventType != "PaymentFailed" && n.EventType != "ReturnRejected"), "new emails visible only to intended recipient");
        Check((await diagnostics.NotificationsAsync(f.OtherSeller, "seller", new(), default)).TotalCount == 0, "other seller cannot see messages");
        await ProductFeedbackChecks.Reject<KeyNotFoundException>(() => diagnostics.NotificationAsync(rejection.Id, f.Seller, "seller", default));
        Check((await diagnostics.NotificationsAsync(f.Buyer, "buyer", new(EventType: "ReturnRejected"), default)).TotalCount == 1, "new event filter works");
        var unknown = new Payment { Id = 9999, OrderId = paid.Id, Status = "Verifying", Method = "PayPal" };
        await new BusinessNotificationService(db, config).PaymentFailedAsync(paid, unknown); await db.SaveChangesAsync();
        Check(!await db.NotificationOutbox.AnyAsync(n => n.EventKey == $"n1:payment:{unknown.Id}:failed:user:{f.Buyer}"), "unknown PayPal status never emits payment failure");
        var before = await db.NotificationOutbox.CountAsync(); var factory = new NotificationProcessorBehaviorChecks.Factory(options); var sender = new FailedSender();
        var processor = new NotificationProcessor(factory, sender, config, clock);
        for (var i = 0; i < 3; i++) { await processor.ProcessDueAsync(100, default); clock.Advance(121); }
        db.ChangeTracker.Clear();
        Check(await db.NotificationOutbox.CountAsync() == before && await db.NotificationOutbox.AllAsync(n => n.Status == "Failed" && n.AttemptsInCycle == 3), "SMTP failure exhausts bounded retries on same rows");
        Check((await db.OrderTables.SingleAsync(o => o.Id == delivered.Id)).Status == "Delivered" && await db.SellerPayouts.AnyAsync(p => p.Id == returned.Id && p.Status == "Returned"), "email failure does not revert business state");
        Check(await processor.RetryFailedAsync(rejection.Id, default), "admin retry reuses failed row");
        config["Mail:Mode"] = "Capture"; await processor.ProcessDueAsync(100, default);
        var final = await db.NotificationOutbox.AsNoTracking().SingleAsync(n => n.Id == rejection.Id);
        Check(final.Status == "Captured" && final.Cycle == 2 && final.SentAt == null, "manual retry capture is not falsely Sent");
        var invalidUser = await db.Users.SingleAsync(u => u.Id == f.OtherBuyer); invalidUser.Email = "invalid-address"; await db.SaveChangesAsync();
        var invalidOrder = await Order(db, f with { Buyer = f.OtherBuyer }); await checkout.CancelUnpaidAsync(invalidOrder.Id);
        Check(invalidOrder.Status == "Cancelled" && await db.NotificationOutbox.AnyAsync(n => n.OrderId == invalidOrder.Id && n.Status == "Failed" && n.LastErrorCode == "RecipientInvalid"),
            "invalid recipient records email error without blocking cancellation");
        await PayPal();
        await LegacyPayPal();
        Console.WriteLine("N1 notifications passed: per-attempt/cycle/recipient idempotency, cancellation/return/payout entry points, privacy, encoded content, unknown PayPal and SMTP failure isolation/retry.");
    }
    private static async Task PayPal()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("n1-paypal-" + Guid.NewGuid()).Options);
        var source = await PayPalCartChecks.Seed(db); var gateway = new PayPalCartChecks.BatchFake { DeclineReference = "order-" + source.Orders[1] };
        var config = Config(); var finance = new SellerFinanceService(db, config); var payments = new PayPalCartPaymentService(db, gateway, config, finance);
        var cart = await payments.StartAsync(source.Buyer, new(source.Key, "partial-mail"));
        await payments.CaptureAsync(source.Buyer, cart.Id, "PP-1"); await payments.ReconcileAsync(source.Buyer, cart.Id);
        Check(await db.NotificationOutbox.CountAsync(n => n.EventType == "PaymentFailed") == 1, "partial PayPal only notifies declined unit once");
        var retry = await payments.StartAsync(source.Buyer, new(source.Key, "cancel-mail")); await payments.CancelAsync(source.Buyer, retry.Id);
        Check(await db.NotificationOutbox.CountAsync(n => n.EventType == "PaymentFailed") == 1, "buyer cancelling approval is not a gateway failure email");
        gateway.LoseCaptureResponse = true;
        var unknown = await payments.StartAsync(source.Buyer, new(source.Key, "unknown-mail"));
        Check((await payments.CaptureAsync(source.Buyer, unknown.Id, "PP-3")).Status == "Verifying", "capture timeout remains uncertain");
        Check(await db.NotificationOutbox.CountAsync(n => n.EventType == "PaymentFailed") == 1, "timeout does not create failed payment email");
        gateway.LoseCaptureResponse = false; gateway.WrongAmount = true; gateway.DeclineReference = null;
        await payments.ReconcileAsync(source.Buyer, unknown.Id);
        Check(await db.NotificationOutbox.CountAsync(n => n.EventType == "PaymentFailed") == 1, "mismatched provider response does not create failed payment email");
        gateway.WrongAmount = false; gateway.DeclineReference = "order-" + source.Orders[1];
        await payments.ReconcileAsync(source.Buyer, unknown.Id);
        Check(await db.NotificationOutbox.CountAsync(n => n.EventType == "PaymentFailed") == 2, "confirmed rejection after unknown response creates exactly one new failure mail");
    }
    internal sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }
    private sealed class FailedSender : IEmailSender
    { public Task SendAsync(EmailEnvelope email, CancellationToken ct) => throw new System.Net.Mail.SmtpException("QA SMTP unavailable"); }
    private static async Task LegacyPayPal()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("n1-legacy-" + Guid.NewGuid()).Options);
        var fixture = await Seed(db);
        foreach (var status in new[] { "DENIED", "FAILED", "PENDING" })
        {
            var order = await Order(db, fixture); var gateway = new LegacyGateway(status);
            var service = new PayPalPaymentService(db, gateway, Config());
            var start = await service.StartAsync(order.Id, "legacy-" + status);
            var result = await service.CaptureAsync(order.Id, start.Id); await service.ReconcileAsync(order.Id);
            var mails = await db.NotificationOutbox.CountAsync(n => n.OrderId == order.Id && n.EventType == "PaymentFailed");
            Check(status == "PENDING" ? result.Status == "Verifying" && mails == 0 : result.Status == "Failed" && mails == 1,
                "single-order PayPal " + status + " distinguishes confirmed failure from uncertainty and replay");
        }
    }
    private sealed class LegacyGateway(string status) : IPayPalGateway
    {
        private readonly string id = "N1-LEGACY-" + Guid.NewGuid().ToString("N");
        public Task<PayPalCreated> CreateAsync(decimal amount, string currency, string requestId, string returnUrl, string cancelUrl, CancellationToken ct) => Task.FromResult(new PayPalCreated(id, "https://www.sandbox.paypal.com/approve"));
        public Task<PayPalCaptured> CaptureAsync(string orderId, string requestId, CancellationToken ct) => GetAsync(orderId, ct);
        public Task<PayPalCaptured> GetAsync(string orderId, CancellationToken ct) => Task.FromResult(new PayPalCaptured(status, null, 0, "USD"));
        public Task<string> RefundAsync(string captureId, decimal amount, string currency, string requestId, CancellationToken ct) => throw new Exception("No refund expected");
    }
}
