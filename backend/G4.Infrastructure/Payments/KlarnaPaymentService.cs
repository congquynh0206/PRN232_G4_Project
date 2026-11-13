using G4.Domain.Entities;
using G4.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace G4.Infrastructure.Payments;

// HPP PLACE_ORDER only authorizes physical goods. A shipment captures the authorized order.
public sealed class KlarnaPaymentService(
    ApplicationDbContext db,
    IKlarnaGateway klarna,
    IConfiguration config,
    ISellerFinanceService finance,
    ILogger<KlarnaPaymentService>? logger = null
)
{
    private static bool IsUsPostalCode(string? value) =>
        value is not null && System.Text.RegularExpressions.Regex.IsMatch(
            value, @"\A[0-9]{5}(-[0-9]{4})?\z");

    private static long Minor(decimal amount)
    {
        if (amount < 0 || decimal.Round(amount, 2) != amount)
            throw new InvalidOperationException("Giá trị thanh toán USD không hợp lệ.");
        return checked((long)(amount * 100m));
    }

    public async Task<KlarnaStarted> StartAsync(int orderId, string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            throw new ArgumentException("Payment key required", nameof(key));
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        var order = await db.OrderTables.Include(x => x.OrderItems).SingleAsync(x => x.Id == orderId, ct);
        if (db.Database.IsRelational()) await db.Entry(order).ReloadAsync(ct);
        var existing = await db.Payments.SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (existing != null)
        {
            if (existing.OrderId != orderId || existing.Method != "Klarna")
                throw new InvalidOperationException("Payment key already used");
            if (existing.Status == "Failed" || existing.KlarnaRedirectUrl == null)
                throw new InvalidOperationException("Lượt Klarna trước không thể tiếp tục. Dùng khóa mới.");
            if (tx != null) await tx.CommitAsync(ct);
            return new(existing.Id, existing.Status!, existing.Status == "Pending" ? existing.KlarnaRedirectUrl : null);
        }
        if (order.Status != "AwaitingPayment" || order.PaymentExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("Order is not payable");
        if (await db.Payments.AnyAsync(p => p.OrderId == orderId && (p.Status == "Succeeded" || p.Status == "Verifying" || p.Status == "Authorized" || p.Status == "Pending" && (p.Method == "Klarna" || p.PayPalCheckoutId != null)), ct))
            throw new InvalidOperationException("Đơn đang có lượt thanh toán khác. Kiểm tra kết quả trước khi thử lại.");
        await finance.ValidateMonthlyLimitAsync(orderId, ct);
        if (order.Currency != "USD" || order.TotalPrice is null or <= 0)
            throw new InvalidOperationException("Klarna hiện chỉ cấu hình cho đơn USD có số tiền dương.");
        var country = order.DeliveryCountrySnapshot?.Trim().ToUpperInvariant();
        if (country is "UNITED STATES" or "USA") country = "US";
        if (country != "US")
            throw new InvalidOperationException(
                "Klarna USD yêu cầu địa chỉ giao hàng tại Hoa Kỳ; không tự đổi quốc gia của buyer.");
        if (!IsUsPostalCode(order.DeliveryPostalCodeSnapshot))
            throw new InvalidOperationException(
                "Klarna yêu cầu ZIP Hoa Kỳ thật: 5 chữ số hoặc ZIP+4.");
        var lines = order.OrderItems.OrderBy(x => x.Id).Select(item => new KlarnaLine(
            item.ProductId?.ToString() ?? throw new InvalidOperationException("Missing product"),
            item.ProductTitleSnapshot ?? throw new InvalidOperationException("Missing title"),
            item.Quantity ?? throw new InvalidOperationException("Missing quantity"),
            Minor(item.UnitPrice ?? throw new InvalidOperationException("Missing price")),
            "physical"
        )).ToList();
        if (lines.Count == 0 || lines.Any(x => x.Quantity <= 0))
            throw new InvalidOperationException("Order lines required");
        if (order.ShippingFee > 0)
            lines.Add(new KlarnaLine($"shipping-{orderId}", "Shipping", 1, Minor(order.ShippingFee), "shipping_fee"));
        var difference = Minor(order.TotalPrice.Value) - lines.Sum(x => checked(x.UnitPriceMinor * x.Quantity));
        if (difference > 0)
            throw new InvalidOperationException("Order amount does not match items, shipping and discounts");
        if (difference < 0)
            lines.Add(new KlarnaLine($"discount-{orderId}", "Discount", 1, difference, "discount"));
        var frontend = (config["Frontend:PublicBaseUrl"] ?? "http://localhost:5131").TrimEnd('/');
        if (!Uri.TryCreate(frontend, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0)
            throw new InvalidOperationException("Frontend:PublicBaseUrl must be an absolute HTTP(S) URL");
        var started = await klarna.StartAsync(new KlarnaCheckoutRequest(
            "US", "en-US", "USD", Minor(order.TotalPrice.Value), lines,
            $"G4-{orderId}",
            $"{frontend}/Buyer/KlarnaReturn?orderId={orderId}",
            $"{frontend}/Buyer/KlarnaCancel?orderId={orderId}",
            new KlarnaAddress(
                order.DeliveryNameSnapshot ?? "",
                order.DeliveryStreetSnapshot ?? "",
                order.DeliveryCitySnapshot ?? "",
                order.DeliveryStateSnapshot?.Trim().ToUpperInvariant().StartsWith("US-", StringComparison.Ordinal) == true
                    ? order.DeliveryStateSnapshot.Trim()[3..].ToUpperInvariant()
                    : order.DeliveryStateSnapshot?.Trim().ToUpperInvariant() ?? "",
                order.DeliveryPostalCodeSnapshot,
                "US",
                null
            )
        ), ct);
        var now = DateTime.UtcNow;
        var payment = new Payment
        {
            OrderId = orderId, UserId = order.BuyerId, Amount = order.TotalPrice,
            Method = "Klarna", Status = "Pending", IdempotencyKey = key,
            KlarnaSessionId = started.HppSessionId, KlarnaSessionUrl = started.SessionUrl,
            KlarnaRedirectUrl = started.RedirectUrl, CreatedAt = now, UpdatedAt = now
        };
        db.Payments.Add(payment);
        // Do not release reserved stock while HPP can still authorize an order.
        // The extra minutes allow maintenance to disable HPP and confirm its final status.
        var hppExpiry = started.ExpiresAt?.UtcDateTime;
        if (hppExpiry <= now)
            throw new InvalidOperationException("Klarna HPP session has already expired");
        order.PaymentExpiresAt = hppExpiry?.AddMinutes(5) ?? now.AddHours(49);
        order.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return new(payment.Id, payment.Status!, started.RedirectUrl);
    }

    public async Task<Payment> ReconcileAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await db.Payments.Where(x => x.OrderId == orderId && x.Method == "Klarna")
            .OrderByDescending(x => x.Id).FirstAsync(ct);
        if (payment.Status is "Succeeded" or "Failed") return payment;
        if (payment.KlarnaSessionUrl == null || payment.KlarnaSessionId == null)
            throw new InvalidOperationException("Klarna HPP session missing");
        var hpp = await klarna.ReadHppAsync(payment.KlarnaSessionUrl, ct);
        if (hpp.Status == "COMPLETED")
        {
            if (string.IsNullOrWhiteSpace(hpp.OrderId))
                throw new InvalidOperationException("Klarna order has not been created");
            var providerOrder = await klarna.ReadOrderAsync(hpp.OrderId, ct);
            await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
            var order = await db.OrderTables.SingleAsync(x => x.Id == orderId, ct);
            if (db.Database.IsRelational()) { await db.Entry(order).ReloadAsync(ct); await db.Entry(payment).ReloadAsync(ct); }
            if (payment.Status == "Succeeded") { if (tx != null) await tx.CommitAsync(ct); return payment; }
            if (providerOrder.OrderId != hpp.OrderId || providerOrder.OrderAmountMinor != Minor(order.TotalPrice ?? 0) || providerOrder.Currency != order.Currency || providerOrder.MerchantReference != $"G4-{orderId}")
                throw new InvalidOperationException("Klarna order does not match the local order");
            if (payment.ProviderOrderId is not null && payment.ProviderOrderId != hpp.OrderId)
                throw new InvalidOperationException("Klarna order identity changed");
            if (order.Status is "Cancelled" or "Expired")
                throw new InvalidOperationException("Order expired before Klarna confirmation; reconcile with provider manually");
            if (await db.Payments.AnyAsync(x => x.OrderId == orderId && x.Id != payment.Id && x.Status == "Succeeded", ct))
                throw new InvalidOperationException("Order was paid by another payment method");
            payment.ProviderOrderId = hpp.OrderId;
            if (providerOrder.CapturedAmountMinor == Minor(order.TotalPrice ?? 0) && providerOrder.CaptureId != null)
            {
                payment.Status = "Succeeded";
                payment.ProviderTransactionId = providerOrder.CaptureId;
                payment.PaidAt = DateTime.UtcNow;
            }
            else if (providerOrder.CapturedAmountMinor == 0 && providerOrder.Status == "AUTHORIZED")
                payment.Status = "Authorized";
            else throw new InvalidOperationException("Klarna provider order is not fully authorized or captured");
            if (order.Status == "AwaitingPayment")
            {
                order.Status = OrderState.Next(order.Status, "PaymentSucceeded");
                if (payment.Status == "Succeeded")
                    await PromotionLifecycle.ConsumeAsync(db, orderId, ct);
            }
            payment.UpdatedAt = DateTime.UtcNow;
            order.UpdatedAt = payment.UpdatedAt.Value;
            await db.SaveChangesAsync(ct);
            if (payment.Status == "Succeeded") await finance.RecordSuccessfulPaymentAsync(orderId, payment.Id, ct);
            if (tx != null) await tx.CommitAsync(ct);
            return payment;
        }
        if (hpp.Status is "CANCELLED" or "DISABLED")
        {
            await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
            if (db.Database.IsRelational()) await db.Entry(payment).ReloadAsync(ct);
            if (payment.Status == "Pending") { payment.Status = "Failed"; payment.ErrorCode = hpp.Status; payment.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
            if (tx != null) await tx.CommitAsync(ct);
        }
        if (hpp.Status is "FAILED" or "ERROR" or "BACK")
        {
            // HPP allows the buyer to retry the same session after a decline or error.
            payment.ErrorCode = hpp.Status;
            payment.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return payment;
    }

    // The buyer came back through the HPP cancel URL. A FAILED/BACK session would stay
    // Pending and block other payment methods until expiry, so disable it at Klarna first
    // (a late authorization can no longer race another method), then confirm the final status.
    public async Task<Payment?> AbandonAsync(int orderId, CancellationToken ct = default)
    {
        if (!await db.Payments.AnyAsync(x => x.OrderId == orderId && x.Method == "Klarna", ct))
            return null;
        var payment = await ReconcileAsync(orderId, ct);
        if (payment.Status != "Pending" || payment.KlarnaSessionUrl == null)
            return payment;
        try
        {
            await klarna.DisableHppAsync(payment.KlarnaSessionUrl, ct);
        }
        catch (HttpRequestException)
        {
            // Another request may already have disabled or completed this session.
            var current = await ReconcileAsync(orderId, ct);
            if (current.Status != "Pending")
                return current;
            throw;
        }
        return await ReconcileAsync(orderId, ct);
    }

    // The buyer may close the Klarna tab without returning. Poll the provider independently.
    // Once the checkout window expires, disable HPP before releasing stock, so a late
    // authorization cannot race with our local order expiry.
    public async Task MaintainAsync(CancellationToken ct = default)
    {
        var ids = await db.Payments.AsNoTracking()
            .Where(p => p.Method == "Klarna" && p.Status == "Pending")
            .OrderBy(p => p.Id).Select(p => p.OrderId).Take(20).ToListAsync(ct);
        foreach (var id in ids)
        {
            if (id is null) continue;
            try
            {
                var payment = await ReconcileAsync(id.Value, ct);
                if (payment.Status != "Pending") continue;
                var expires = await db.OrderTables.Where(o => o.Id == id.Value)
                    .Select(o => o.PaymentExpiresAt).SingleAsync(ct);
                if (expires > DateTime.UtcNow || payment.KlarnaSessionUrl == null) continue;
                await klarna.DisableHppAsync(payment.KlarnaSessionUrl, ct);
                await ReconcileAsync(id.Value, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Other attempts must still be checked; the next cycle retries this one.
                logger?.LogWarning(ex, "Klarna HPP reconciliation pending for order {OrderId}", id);
            }
        }

        var captures = await db.Payments.AsNoTracking()
            .Where(p => p.Method == "Klarna" && p.Status == "Verifying")
            .OrderBy(p => p.Id).Select(p => p.OrderId).Take(20).ToListAsync(ct);
        foreach (var id in captures)
        {
            if (id is null) continue;
            try { await CaptureForShipmentAsync(id.Value, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger?.LogWarning(ex, "Klarna capture reconciliation pending for order {OrderId}", id);
            }
        }
    }

    // Cancel an uncaptured provider authorization instead of issuing a refund for money never captured.
    // The caller holds the order transaction and changes the order to Cancelled afterwards.
    public async Task<bool> CancelAuthorizationAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(x => x.OrderId == orderId && x.Method == "Klarna" && x.Status == "Authorized", ct);
        if (payment == null) return false;
        if (payment.ProviderOrderId == null) throw new InvalidOperationException("Klarna order missing");
        await klarna.CancelOrderAsync(payment.ProviderOrderId, $"klarna-cancel-{payment.Id}", ct);
        payment.Status = "Failed";
        payment.ErrorCode = "CANCELLED";
        payment.UpdatedAt = DateTime.UtcNow;
        var order = await db.OrderTables.Include(x => x.OrderItems).SingleAsync(x => x.Id == orderId, ct);
        foreach (var line in order.OrderItems)
        {
            var stock = await db.Inventories.SingleAsync(x => x.ProductId == line.ProductId, ct);
            stock.Quantity += line.Quantity;
            stock.LastUpdated = DateTime.UtcNow;
        }
        await PromotionLifecycle.ReleaseAsync(db, orderId, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // Called before the carrier receives an outbound order. A timeout remains Verifying;
    // querying the provider on retry prevents an accidental second capture.
    public async Task<Payment?> CaptureForShipmentAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(x => x.OrderId == orderId && x.Method == "Klarna" && (x.Status == "Authorized" || x.Status == "Verifying" || x.Status == "Succeeded"), ct);
        if (payment == null || payment.Status == "Succeeded") return payment;
        if (payment.ProviderOrderId == null) throw new InvalidOperationException("Klarna order missing");
        await using (var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct))
        {
            if (db.Database.IsRelational()) await db.Entry(payment).ReloadAsync(ct);
            if (payment.Status == "Authorized")
            {
                await finance.ValidateMonthlyLimitAsync(orderId, ct);
                payment.Status = "Verifying";
                payment.CaptureReservedAt = DateTime.UtcNow;
                var order = await db.OrderTables.SingleAsync(x => x.Id == orderId, ct);
                if (order.Status is not ("Paid" or "Preparing"))
                    throw new InvalidOperationException("Order is not ready for Klarna capture");
                payment.ReservedSellerGross = order.PricingSchemaVersion == 1 ? order.SellerGrossSnapshot : order.TotalPrice;
                payment.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            if (tx != null) await tx.CommitAsync(ct);
        }
        var current = await klarna.ReadOrderAsync(payment.ProviderOrderId, ct);
        if (current.CapturedAmountMinor == 0)
        {
            try { await klarna.CaptureAsync(payment.ProviderOrderId, Minor(payment.Amount ?? 0), $"klarna-capture-{payment.Id}", ct); }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
            { return payment; }
            current = await klarna.ReadOrderAsync(payment.ProviderOrderId, ct);
        }
        if (current.CapturedAmountMinor != Minor(payment.Amount ?? 0) || current.CaptureId == null)
            return payment;
        await using var finalTx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        if (db.Database.IsRelational()) await db.Entry(payment).ReloadAsync(ct);
        if (payment.Status != "Succeeded")
        {
            payment.Status = "Succeeded";
            payment.ProviderTransactionId = current.CaptureId;
            payment.PaidAt = DateTime.UtcNow;
            payment.UpdatedAt = payment.PaidAt;
            await PromotionLifecycle.ConsumeAsync(db, orderId, ct);
            await db.SaveChangesAsync(ct);
            await finance.RecordSuccessfulPaymentAsync(orderId, payment.Id, ct);
        }
        if (finalTx != null) await finalTx.CommitAsync(ct);
        return payment;
    }
}

public sealed record KlarnaStarted(int Id, string Status, string? RedirectUrl);
