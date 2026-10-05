using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;
using G4.Application.Integrations;
using G4.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
internal static class PromotionCheckoutChecks
{
    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.AddRange(new User { Id = 1, Role = "buyer" }, new User { Id = 2, Role = "seller" }, new User { Id = 3, Role = "admin" }, new Product { Id = 1, SellerId = 2, Title = "Camera", Price = 100, WeightKg = 1 }, new Inventory { Id = 1, ProductId = 1, Quantity = 10 }, new Address { Id = 1, UserId = 1, State = "Hanoi", Country = "Vietnam" }, new Address { Id = 2, UserId = 2, State = "Hanoi", Country = "Vietnam", IsDefault = true }); await db.SaveChangesAsync();
        var management = new PromotionManagementService(db); var input = new PromotionInput { Name = "Coupon", Type = "Coupon", Code = "SINGLE", Value = 10, IsPercent = true, MaxUsage = 1, StartAt = DateTime.UtcNow.AddHours(-1), EndAt = DateTime.UtcNow.AddDays(1) };
        var coupon = await management.SaveAsync(2, false, input);
        var checkout = new CheckoutService(db); var request = new CheckoutRequest(1, [new(1, 1)], " single ", "promo-order");
        var quote = await checkout.QuoteAsync(1, request);
        Check(quote.Total == 92 && quote.SellerDiscount == 10 && quote.PricingFingerprint?.Length == 64, "seller coupon quote and fingerprint");
        Check(!await db.PromotionUsages.AnyAsync(), "quote must not reserve usage");
        var order = await checkout.CreateOrderAsync(1, request with { ExpectedTotal = quote.Total, PricingFingerprint = quote.PricingFingerprint });
        Check(order.CouponCode == "SINGLE" && order.PricingSchemaVersion == 1, "normalized coupon and schema snapshot");
        Check((await db.PromotionUsages.SingleAsync()).State == "Reserved", "create reserves coupon");
        Check(await db.OrderPromotionSnapshots.CountAsync() == 1, "create immutable promotion snapshot");
        await Reject(() => checkout.CreateOrderAsync(1, request with { CheckoutKey = "second" }), "last coupon slot reserved");
        var paused = await management.SetStateAsync(2, false, coupon.Id, new(true, null, coupon.Version));
        await checkout.PayCardAsync(order.Id, "4111111111111111", "12/30", "pay-promo");
        Check((await db.PromotionUsages.SingleAsync()).State == "Consumed", "payment honors paused snapshot and consumes once");
        await checkout.PayCardAsync(order.Id, "4111111111111111", "12/30", "pay-promo");
        Check(await db.PromotionUsages.CountAsync() == 1, "payment retry idempotence");
        var sale = await management.SaveAsync(2, false, input with { Name = "Sale", Type = "Sale", Code = null, MaxUsage = null, Value = 20 });
        var fresh = request with { CouponCode = null, CheckoutKey = "sale-order" }; var stale = await checkout.QuoteAsync(1, fresh);
        await management.SaveAsync(2, false, input with { Name = "Sale", Type = "Sale", Code = null, MaxUsage = null, Value = 25, Version = sale.Version }, sale.Id);
        await Reject(() => checkout.CreateOrderAsync(1, fresh with { ExpectedTotal = stale.Total, PricingFingerprint = stale.PricingFingerprint }), "stale quote must not create order");
        var cancelled = await checkout.CreateOrderAsync(1, fresh); await checkout.CancelUnpaidAsync(cancelled.Id);
        Check((await db.PromotionUsages.SingleAsync(x => x.OrderId == cancelled.Id)).State == "Released", "unpaid cancel releases budget");
        var addressQuote = await checkout.QuoteAsync(1, fresh); (await db.Addresses.FindAsync(1))!.Street = "Changed street"; await db.SaveChangesAsync();
        Check((await checkout.QuoteAsync(1, fresh)).PricingFingerprint != addressQuote.PricingFingerprint, "street edit invalidates quote even at same shipping fee");
        var paypalOrder = await checkout.CreateOrderAsync(1, fresh with { CheckoutKey = "paypal-promo" });
        var gateway = new StalePayPalGateway(); var paypal = new PayPalPaymentService(db, gateway, new ConfigurationBuilder().Build());
        await paypal.StartAsync(paypalOrder.Id, "paypal-start");
        await paypal.CaptureAsync(paypalOrder.Id, "PP-PROMO");
        paypalOrder.PaymentExpiresAt = DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
        await checkout.ExpirePendingAsync(); Check(paypalOrder.Status == "AwaitingPayment", "unknown PayPal retains order and usage after deadline");
        await Reject(() => checkout.CancelUnpaidAsync(paypalOrder.Id), "unknown PayPal cannot be cancelled");
        await Reject(() => paypal.StartAsync(paypalOrder.Id, "new-paypal-attempt"), "unknown PayPal cannot be replaced");
        gateway.BeforeGet = async () => { var payment = await db.Payments.SingleAsync(p => p.OrderId == paypalOrder.Id); payment.Status = "Succeeded"; payment.ProviderTransactionId = "PP-CAPTURE"; paypalOrder.Status = "Paid"; await db.SaveChangesAsync(); };
        var reconciled = await paypal.ReconcileAsync(paypalOrder.Id); Check(reconciled.Status == "Succeeded", "stale PayPal response cannot downgrade success");
        Check((await db.PromotionUsages.SingleAsync(x => x.OrderId == paypalOrder.Id)).State == "Consumed", "reconcile consumes reserved usage once");
        await management.SaveAsync(2, false, input with { Name = "Full Sale", Type = "Sale", Code = null, MaxUsage = null, Value = 100 });
        var zeroCoupon = await management.SaveAsync(2, false, input with { Code = "ZEROBENEFIT", Value = 10, IsPercent = false });
        var zeroRequest = fresh with { CouponCode = "ZEROBENEFIT", CheckoutKey = "zero-benefit" }; var zeroBenefit = await checkout.CreateOrderAsync(1, zeroRequest);
        Check((await checkout.CreateOrderAsync(1, zeroRequest)).Id == zeroBenefit.Id, "zero-benefit coupon keeps checkout retry identity");
        Check((await db.PromotionUsages.SingleAsync(x => x.OrderId == zeroBenefit.Id && x.PromotionId == zeroCoupon.Id)).Amount == 0, "zero-benefit coupon still reserves its count");
        Console.WriteLine("Promotion checkout snapshot, reservation, stale quote and payment checks passed");
    }
    private static void Check(bool condition, string message) => ReturnAutomationChecks.Check(condition, message);
    private static async Task Reject(Func<Task> work, string message) { try { await work(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; } throw new Exception(message); }
    private sealed class StalePayPalGateway : IPayPalGateway
    {
        public Func<Task>? BeforeGet { get; set; }
        public Task<PayPalCreated> CreateAsync(decimal amount, string currency, string key, string returnUrl, string cancelUrl, CancellationToken ct) => Task.FromResult(new PayPalCreated("PP-PROMO", "https://example.test"));
        public Task<PayPalCaptured> CaptureAsync(string id, string key, CancellationToken ct) => throw new HttpRequestException("capture outcome unknown");
        public async Task<PayPalCaptured> GetAsync(string id, CancellationToken ct) { if (BeforeGet != null) await BeforeGet(); return new("CREATED", null, 0, "USD"); }
        public Task<string> RefundAsync(string id, decimal amount, string currency, string key, CancellationToken ct) => throw new NotSupportedException();
    }
}
