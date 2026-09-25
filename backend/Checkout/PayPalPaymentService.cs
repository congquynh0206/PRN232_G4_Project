using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Checkout;

public sealed class PayPalPaymentService(ApplicationDbContext db, IPayPalGateway paypal, IConfiguration config)
{
    public async Task<PayPalCreated> StartAsync(int orderId, string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new ArgumentException("Payment key required");
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        if (order.Status != "AwaitingPayment" || order.PaymentExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("Order is not payable");
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.IdempotencyKey == key, ct);
        if (payment is not null && payment.OrderId != orderId) throw new InvalidOperationException("Payment key already used");
        var frontendBase = (config["Frontend:PublicBaseUrl"] ?? "http://localhost:5000").TrimEnd('/');
        var created = await paypal.CreateAsync(order.TotalPrice!.Value, order.Currency, key,
            $"{frontendBase}/Demo/PayPalReturn?orderId={orderId}",
            $"{frontendBase}/Demo/PayPalCancel?orderId={orderId}", ct);
        if (payment is null)
        {
            payment = new Payment
            {
                OrderId = orderId, UserId = order.BuyerId, Amount = order.TotalPrice,
                Method = "PayPal", Status = "Pending", ProviderOrderId = created.Id,
                IdempotencyKey = key, CreatedAt = DateTime.UtcNow
            };
            db.Payments.Add(payment);
        }
        else payment.ProviderOrderId = created.Id;
        await db.SaveChangesAsync(ct);
        return created;
    }

    public async Task<Payment> CaptureAsync(int orderId, string providerOrderId, CancellationToken ct = default)
    {
        var payment = await db.Payments.SingleAsync(p => p.OrderId == orderId && p.ProviderOrderId == providerOrderId && p.Method == "PayPal", ct);
        if (payment.Status == "Succeeded") return payment;
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        if (order.Status != "AwaitingPayment") throw new InvalidOperationException("Order is not awaiting payment");
        PayPalCaptured capture;
        try { capture = await paypal.CaptureAsync(providerOrderId, $"capture-{payment.Id}", ct); }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            payment.Status = "Verifying";
            await db.SaveChangesAsync(ct);
            return payment;
        }
        return await ApplyCaptureAsync(order, payment, capture, ct);
    }

    public async Task<Payment> ReconcileAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await db.Payments.Where(p => p.OrderId == orderId && p.Method == "PayPal")
            .OrderByDescending(p => p.Id).FirstAsync(ct);
        if (payment.Status == "Succeeded") return payment;
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        var capture = await paypal.GetAsync(payment.ProviderOrderId!, ct);
        return await ApplyCaptureAsync(order, payment, capture, ct);
    }

    private async Task<Payment> ApplyCaptureAsync(OrderTable order, Payment payment, PayPalCaptured capture, CancellationToken ct)
    {
        if (capture.Status == "COMPLETED" && capture.CaptureId is not null &&
            capture.Amount == order.TotalPrice && capture.Currency == order.Currency)
        {
            payment.Status = "Succeeded";
            payment.ProviderTransactionId = capture.CaptureId;
            payment.PaidAt = DateTime.UtcNow;
            if (order.Status == "AwaitingPayment")
            {
                order.Status = OrderState.Next(order.Status, "PaymentSucceeded");
                await new MvpService(db).QueueEmailAsync(order, "PaymentSucceeded", "Payment confirmed",
                    $"Order {order.Id} was paid successfully.", ct);
            }
        }
        else if (capture.Status is "VOIDED" or "DECLINED") payment.Status = "Failed";
        else payment.Status = "Verifying";
        payment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return payment;
    }
}
