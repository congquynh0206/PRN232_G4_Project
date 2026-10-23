using G4.Domain.Entities;
using G4.Infrastructure.Diagnostics;
using G4.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Payments;

public sealed class PayPalPaymentService(
    ApplicationDbContext db,
    IPayPalGateway paypal,
    IConfiguration config,
    ISellerFinanceService? finance = null,
    IPayPalCartPaymentService? carts = null
) : IPayPalPaymentService
{
    private ISellerFinanceService Finance => finance ?? new SellerFinanceService(db, config);

    public async Task<PayPalCreated> StartAsync(
        int orderId,
        string key,
        CancellationToken ct = default
    )
    {
        using var diagnostic = IntegrationContext.ForOrder(orderId);
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            throw new ArgumentException("Payment key required");
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        if (db.Database.IsRelational())
            await db.Entry(order).ReloadAsync(ct);
        if (
            await db.Payments.AnyAsync(
                x => x.OrderId == orderId && x.PayPalCheckoutId != null && x.Status == "Pending",
                ct
            )
        )
            throw new InvalidOperationException(
                "Đơn nằm trong lượt PayPal cả giỏ. Mở kết quả thanh toán cả giỏ để tiếp tục hoặc hủy lượt đó trước."
            );
        if (await db.Payments.AnyAsync(x => x.OrderId == orderId && x.Status == "Verifying", ct))
            throw new InvalidOperationException(
                "PayPal đang xác minh thanh toán. Hãy chờ trước khi tạo lần thanh toán khác."
            );
        if (order.Status != "AwaitingPayment" || order.PaymentExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("Order is not payable");
        await Finance.ValidateMonthlyLimitAsync(orderId, ct);
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.IdempotencyKey == key, ct);
        if (payment is not null && (payment.OrderId != orderId || payment.Method != "PayPal"))
            throw new InvalidOperationException("Payment key already used");
        if (payment?.Status == "Failed")
            throw new InvalidOperationException(
                "Lần thanh toán đã thất bại. Hãy thử lại bằng khóa thanh toán mới."
            );
        var frontendBase = (config["Frontend:PublicBaseUrl"] ?? "http://localhost:5000").TrimEnd(
            '/'
        );
        var created = await paypal.CreateAsync(
            order.TotalPrice!.Value,
            order.Currency,
            key,
            $"{frontendBase}/Buyer/PayPalReturn?orderId={orderId}",
            $"{frontendBase}/Buyer/PayPalCancel?orderId={orderId}",
            ct
        );
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
                CreatedAt = DateTime.UtcNow,
            };
            db.Payments.Add(payment);
        }
        else
            payment.ProviderOrderId = created.Id;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return created;
    }

    public async Task<Payment> CaptureAsync(
        int orderId,
        string providerOrderId,
        CancellationToken ct = default
    )
    {
        var cartPayment = await db
            .Payments.AsNoTracking()
            .SingleOrDefaultAsync(
                x =>
                    x.OrderId == orderId
                    && x.ProviderOrderId == providerOrderId
                    && x.PayPalCheckoutId != null,
                ct
            );
        if (cartPayment != null)
        {
            if (carts == null)
                throw new InvalidOperationException("Cart PayPal service unavailable");
            await carts.CaptureAsync(
                cartPayment.UserId!.Value,
                cartPayment.PayPalCheckoutId!.Value,
                providerOrderId,
                ct
            );
            return await db.Payments.SingleAsync(x => x.Id == cartPayment.Id, ct);
        }
        using var diagnostic = IntegrationContext.ForOrder(orderId);
        var reconcileOnly = false;
        // Hold usage while a capture is in flight or its result is unknown.
        await using (var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct))
        {
            var current = await db.OrderTables.SingleAsync(x => x.Id == orderId, ct);
            if (db.Database.IsRelational())
                await db.Entry(current).ReloadAsync(ct);
            var attempt = await db.Payments.SingleAsync(
                x =>
                    x.OrderId == orderId
                    && x.ProviderOrderId == providerOrderId
                    && x.Method == "PayPal",
                ct
            );
            if (db.Database.IsRelational())
                await db.Entry(attempt).ReloadAsync(ct);
            if (attempt.Status is not ("Succeeded" or "Failed"))
            {
                if (
                    await db.Payments.AnyAsync(
                        x =>
                            x.OrderId == orderId
                            && x.Id != attempt.Id
                            && (x.Status == "Verifying" || x.Status == "Succeeded"),
                        ct
                    )
                )
                    throw new InvalidOperationException(
                        "Một lần thanh toán PayPal khác đang được xác minh."
                    );
                if (
                    current.Status != "AwaitingPayment"
                    || current.PaymentExpiresAt <= DateTime.UtcNow && attempt.Status != "Verifying"
                )
                    throw new InvalidOperationException("Order is not payable");
                if (attempt.Status == "Verifying")
                {
                    // A legacy unknown attempt is queried, never retroactively
                    // authorized for a fresh capture under today's capacity.
                    reconcileOnly =
                        attempt.CaptureReservedAt == null || attempt.ReservedSellerGross == null;
                }
                else
                {
                    // The seller gate acquired by validation lives until this
                    // outer transaction commits both the reservation and state.
                    await Finance.ValidateMonthlyLimitAsync(orderId, ct);
                    attempt.CaptureReservedAt = DateTime.UtcNow;
                    attempt.ReservedSellerGross =
                        current.PricingSchemaVersion == 1
                            ? current.SellerGrossSnapshot
                            : current.TotalPrice;
                }
                attempt.Status = "Verifying";
                attempt.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            if (tx != null)
                await tx.CommitAsync(ct);
        }

        var payment = await db.Payments.SingleAsync(
            p =>
                p.OrderId == orderId
                && p.ProviderOrderId == providerOrderId
                && p.Method == "PayPal",
            ct
        );
        if (payment.Status == "Failed")
            return payment;
        if (payment.Status == "Succeeded")
        {
            await PromotionLifecycle.ConsumeAsync(db, orderId, ct);
            await db.SaveChangesAsync(ct);
            await Finance.RecordSuccessfulPaymentAsync(orderId, payment.Id, ct);
            return payment;
        }

        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        if (order.Status != "AwaitingPayment")
            throw new InvalidOperationException("Order is not awaiting payment");
        PayPalCaptured capture;
        try
        {
            capture = reconcileOnly
                ? await paypal.GetAsync(providerOrderId, ct)
                : await paypal.CaptureAsync(providerOrderId, $"capture-{payment.Id}", ct);
        }
        catch (Exception ex)
            when (ex is HttpRequestException
                || ex is TaskCanceledException && !ct.IsCancellationRequested
            )
        {
            await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
            if (db.Database.IsRelational())
                await db.Entry(payment).ReloadAsync(ct);
            if (payment.Status is not ("Succeeded" or "Failed"))
                payment.Status = "Verifying";
            else if (payment.Status == "Succeeded")
            {
                await PromotionLifecycle.ConsumeAsync(db, orderId, ct);
                await Finance.RecordSuccessfulPaymentAsync(orderId, payment.Id, ct);
            }

            await db.SaveChangesAsync(ct);
            if (tx != null)
                await tx.CommitAsync(ct);
            return payment;
        }

        return await ApplyCaptureAsync(order, payment, capture, ct);
    }

    public async Task<Payment> ReconcileAsync(int orderId, CancellationToken ct = default)
    {
        using var diagnostic = IntegrationContext.ForOrder(orderId);
        var payment = await db
            .Payments.Where(p => p.OrderId == orderId && p.Method == "PayPal")
            .OrderByDescending(p => p.Status == "Verifying")
            .ThenByDescending(p => p.Id)
            .FirstAsync(ct);
        if (payment.PayPalCheckoutId != null)
        {
            if (carts == null)
                throw new InvalidOperationException("Cart PayPal service unavailable");
            await carts.ReconcileAsync(payment.UserId!.Value, payment.PayPalCheckoutId.Value, ct);
            return payment;
        }
        if (payment.Status == "Failed")
            return payment;
        if (payment.Status == "Succeeded")
        {
            await PromotionLifecycle.ConsumeAsync(db, orderId, ct);
            await db.SaveChangesAsync(ct);
            await Finance.RecordSuccessfulPaymentAsync(orderId, payment.Id, ct);
            return payment;
        }

        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        var capture = await paypal.GetAsync(payment.ProviderOrderId!, ct);
        return await ApplyCaptureAsync(order, payment, capture, ct);
    }

    private async Task<Payment> ApplyCaptureAsync(
        OrderTable order,
        Payment payment,
        PayPalCaptured capture,
        CancellationToken ct
    )
    {
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, order.Id, ct);
        if (db.Database.IsRelational())
        {
            await db.Entry(order).ReloadAsync(ct);
            await db.Entry(payment).ReloadAsync(ct);
        }

        if (payment.Status is "Succeeded" or "Failed")
        {
            if (payment.Status == "Succeeded")
            {
                await PromotionLifecycle.ConsumeAsync(db, order.Id, ct);
                await db.SaveChangesAsync(ct);
                await Finance.RecordSuccessfulPaymentAsync(order.Id, payment.Id, ct);
            }
            if (tx != null)
                await tx.CommitAsync(ct);
            return payment;
        }

        if (order.Status is "Cancelled" or "Expired")
            throw new InvalidOperationException("Order is no longer payable");
        if (
            capture.Status == "COMPLETED"
            && capture.CaptureId is not null
            && capture.Amount == order.TotalPrice
            && capture.Currency == order.Currency
        )
        {
            payment.Status = "Succeeded";
            payment.ProviderTransactionId = capture.CaptureId;
            payment.PaidAt = DateTime.UtcNow;
            await PromotionLifecycle.ConsumeAsync(db, order.Id, ct);
            if (order.Status == "AwaitingPayment")
            {
                order.Status = OrderState.Next(order.Status, "PaymentSucceeded");
                await new CheckoutService(db, config: config).QueueEmailAsync(
                    order,
                    "PaymentSucceeded",
                    "Thanh toán thành công",
                    $"Đơn hàng #{order.Id} đã được thanh toán thành công.",
                    ct
                );
            }
        }
        else if (capture.Status is "VOIDED" or "DECLINED" or "DENIED" or "FAILED")
        {
            payment.Status = "Failed";
            await new G4.Infrastructure.Notifications.BusinessNotificationService(
                db,
                config
            ).PaymentFailedAsync(order, payment, ct);
        }
        else
            payment.Status = "Verifying";
        payment.UpdatedAt = DateTime.UtcNow;
        order.UpdatedAt = payment.UpdatedAt.Value;
        await db.SaveChangesAsync(ct);
        if (payment.Status == "Succeeded")
            await Finance.RecordSuccessfulPaymentAsync(order.Id, payment.Id, ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return payment;
    }
}
