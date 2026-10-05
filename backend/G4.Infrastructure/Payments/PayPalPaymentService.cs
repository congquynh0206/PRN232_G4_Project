using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using G4.Infrastructure.Promotions;
using G4.Infrastructure.Diagnostics;

namespace G4.Infrastructure.Payments;

public sealed class PayPalPaymentService(ApplicationDbContext db, IPayPalGateway paypal, IConfiguration config,
    ISellerFinanceService? finance = null) : IPayPalPaymentService
{
    public async Task<PayPalCreated> StartAsync(int orderId, string key, CancellationToken ct = default)
    {
        using var diagnostic = IntegrationContext.ForOrder(orderId);
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new ArgumentException("Payment key required");
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        if (db.Database.IsRelational()) await db.Entry(order).ReloadAsync(ct);
        if (await db.Payments.AnyAsync(x => x.OrderId == orderId && x.Status == "Verifying", ct)) throw new InvalidOperationException("PayPal đang xác minh thanh toán. Hãy chờ trước khi tạo lần thanh toán khác.");
        if (order.Status != "AwaitingPayment" || order.PaymentExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("Order is not payable");
        if (finance is not null) await finance.ValidateMonthlyLimitAsync(orderId, ct);
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.IdempotencyKey == key, ct);
        if (payment is not null && payment.OrderId != orderId) throw new InvalidOperationException("Payment key already used");
        var frontendBase = (config["Frontend:PublicBaseUrl"] ?? "http://localhost:5000").TrimEnd('/');
        var created = await paypal.CreateAsync(order.TotalPrice!.Value, order.Currency, key,
            $"{frontendBase}/Buyer/PayPalReturn?orderId={orderId}",
            $"{frontendBase}/Buyer/PayPalCancel?orderId={orderId}", ct);
        if (payment is null)
        {
            payment = new Payment
            {
                OrderId = orderId,
                UserId = order.BuyerId,
                Amount = order.TotalPrice,
                Method = "PayPal",
                Status = "Pending",
                ProviderOrderId = created.Id,
                IdempotencyKey = key,
                CreatedAt = DateTime.UtcNow
            };
            db.Payments.Add(payment);
        }
        else payment.ProviderOrderId = created.Id;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return created;
    }

    public async Task<Payment> CaptureAsync(int orderId, string providerOrderId, CancellationToken ct = default)
    {
        using var diagnostic = IntegrationContext.ForOrder(orderId);
        // Hold usage while a capture is in flight or its result is unknown.
        await using (var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct))
        {
            var current = await db.OrderTables.SingleAsync(x => x.Id == orderId, ct);
            if (db.Database.IsRelational()) await db.Entry(current).ReloadAsync(ct);
            var attempt = await db.Payments.SingleAsync(x => x.OrderId == orderId && x.ProviderOrderId == providerOrderId && x.Method == "PayPal", ct);
            if (attempt.Status != "Succeeded")
            {
                if (await db.Payments.AnyAsync(x => x.OrderId == orderId && x.Id != attempt.Id && x.Status == "Verifying", ct)) throw new InvalidOperationException("Một lần thanh toán PayPal khác đang được xác minh.");
                if (current.Status != "AwaitingPayment" || current.PaymentExpiresAt <= DateTime.UtcNow && attempt.Status != "Verifying") throw new InvalidOperationException("Order is not payable");
                attempt.Status = "Verifying"; await db.SaveChangesAsync(ct);
            }
            if (tx != null) await tx.CommitAsync(ct);
        }
        var payment = await db.Payments.SingleAsync(p => p.OrderId == orderId && p.ProviderOrderId == providerOrderId && p.Method == "PayPal", ct);
        if (payment.Status == "Succeeded")
        {
            await PromotionLifecycle.ConsumeAsync(db, orderId, ct); await db.SaveChangesAsync(ct);
            if (finance is not null) await finance.RecordSuccessfulPaymentAsync(orderId, payment.Id, ct);
            return payment;
        }
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        if (order.Status != "AwaitingPayment") throw new InvalidOperationException("Order is not awaiting payment");
        if (finance is not null) await finance.ValidateMonthlyLimitAsync(orderId, ct);
        PayPalCaptured capture;
        try { capture = await paypal.CaptureAsync(providerOrderId, $"capture-{payment.Id}", ct); }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
            if (db.Database.IsRelational()) await db.Entry(payment).ReloadAsync(ct);
            if (payment.Status != "Succeeded") payment.Status = "Verifying";
            else
            {
                await PromotionLifecycle.ConsumeAsync(db, orderId, ct);
                if (finance != null) await finance.RecordSuccessfulPaymentAsync(orderId, payment.Id, ct);
            }
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return payment;
        }
        return await ApplyCaptureAsync(order, payment, capture, ct);
    }

    public async Task<Payment> ReconcileAsync(int orderId, CancellationToken ct = default)
    {
        using var diagnostic = IntegrationContext.ForOrder(orderId);
        var payment = await db.Payments.Where(p => p.OrderId == orderId && p.Method == "PayPal")
            .OrderByDescending(p => p.Status == "Verifying").ThenByDescending(p => p.Id).FirstAsync(ct);
        if (payment.Status == "Succeeded")
        {
            await PromotionLifecycle.ConsumeAsync(db, orderId, ct); await db.SaveChangesAsync(ct);
            if (finance is not null) await finance.RecordSuccessfulPaymentAsync(orderId, payment.Id, ct);
            return payment;
        }
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        var capture = await paypal.GetAsync(payment.ProviderOrderId!, ct);
        return await ApplyCaptureAsync(order, payment, capture, ct);
    }

    private async Task<Payment> ApplyCaptureAsync(OrderTable order, Payment payment, PayPalCaptured capture, CancellationToken ct)
    {
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, order.Id, ct);
        if (db.Database.IsRelational()) { await db.Entry(order).ReloadAsync(ct); await db.Entry(payment).ReloadAsync(ct); }
        if (payment.Status == "Succeeded")
        {
            await PromotionLifecycle.ConsumeAsync(db, order.Id, ct); await db.SaveChangesAsync(ct);
            if (finance != null) await finance.RecordSuccessfulPaymentAsync(order.Id, payment.Id, ct);
            if (tx != null) await tx.CommitAsync(ct); return payment;
        }
        if (order.Status is "Cancelled" or "Expired") throw new InvalidOperationException("Order is no longer payable");
        if (capture.Status == "COMPLETED" && capture.CaptureId is not null &&
            capture.Amount == order.TotalPrice && capture.Currency == order.Currency)
        {
            payment.Status = "Succeeded";
            payment.ProviderTransactionId = capture.CaptureId;
            payment.PaidAt = DateTime.UtcNow;
            await PromotionLifecycle.ConsumeAsync(db, order.Id, ct);
            if (order.Status == "AwaitingPayment")
            {
                order.Status = OrderState.Next(order.Status, "PaymentSucceeded");
                await new CheckoutService(db, config: config).QueueEmailAsync(order, "PaymentSucceeded", "Thanh toán thành công",
                    $"Đơn hàng #{order.Id} đã được thanh toán thành công.", ct);
            }
        }
        else if (capture.Status is "VOIDED" or "DECLINED") payment.Status = "Failed";
        else payment.Status = "Verifying";
        payment.UpdatedAt = DateTime.UtcNow;
        order.UpdatedAt = payment.UpdatedAt.Value;
        await db.SaveChangesAsync(ct);
        if (payment.Status == "Succeeded" && finance is not null)
            await finance.RecordSuccessfulPaymentAsync(order.Id, payment.Id, ct);
        if (tx != null) await tx.CommitAsync(ct);
        return payment;
    }
}
