using System.Data;
using System.Text.Json;
using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Diagnostics;
using G4.Infrastructure.Notifications;
using G4.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Checkout;

public sealed class CheckoutService(
    ApplicationDbContext db,
    ISellerFinanceService? finance = null,
    IConfiguration? config = null,
    IIntegrationLogWriter? logs = null,
    TimeProvider? clock = null
) : ICheckoutService
{
    public async Task<IReadOnlyList<Product>> RandomProductsAsync(
        int count,
        CancellationToken ct = default
    )
    {
        if (count is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(count));
        var now = DateTime.UtcNow;
        var products = await db
            .Products.AsNoTracking()
            .Where(p =>
                p.SellerId != null
                && p.IsListed
                && !p.ImmediatePaymentRequired
                && p.IsAuction != true
                && p.Price != null
                && p.WeightKg > 0
                && db.Addresses.Any(a => a.UserId == p.SellerId && a.IsDefault == true)
            )
            .Where(p =>
                !db.SellerAccounts.Any(a =>
                    a.SellerId == p.SellerId
                    && (
                        a.Status != "Active"
                        || a.PermanentSalesBlockedAt != null
                        || a.NegativeBalance > 0 && a.DebtDueAt <= now
                    )
                )
            )
            .Join(
                db.Inventories.Where(i => i.Quantity > 0),
                p => p.Id,
                i => i.ProductId,
                (p, i) => p
            )
            .ToListAsync(ct);
        var bySeller = products.GroupBy(p => p.SellerId).FirstOrDefault(g => g.Count() >= count);
        if (bySeller is null)
            throw new InvalidOperationException("Not enough in-stock products from one seller");
        return bySeller.OrderBy(_ => Random.Shared.Next()).Take(count).ToArray();
    }

    public async Task<PriceQuote> QuoteAsync(
        int buyerId,
        CheckoutRequest request,
        CancellationToken ct = default
    )
    {
        var (quote, _, _, _) = await ValidateAndQuoteAsync(buyerId, request, ct);
        return quote;
    }

    public async Task<OrderTable> CreateOrderAsync(
        int buyerId,
        CheckoutRequest request,
        CancellationToken ct = default
    )
        => await CreateOrderCoreAsync(buyerId, request, false, ct);

    // Only the immediate checkout orchestrator can opt in, inside its SQL transaction.
    internal Task<OrderTable> CreateCapturedImmediateOrderAsync(
        int buyerId,
        CheckoutRequest request,
        ImmediatePaymentAttempt attempt,
        CancellationToken ct
    )
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Immediate payment requires an atomic transaction.");
        if (attempt.Stage != "Captured" || !attempt.InventoryHeld
            || string.IsNullOrWhiteSpace(attempt.CaptureId)
            || attempt.Amount != request.ExpectedTotal)
            throw new InvalidOperationException("A verified capture and inventory claim are required.");
        return CreateOrderCoreAsync(buyerId, request, true, ct, attempt);
    }

    private async Task<OrderTable> CreateOrderCoreAsync(
        int buyerId,
        CheckoutRequest request,
        bool immediate,
        CancellationToken ct,
        ImmediatePaymentAttempt? capturedAttempt = null
    )
    {
        if (string.IsNullOrWhiteSpace(request.CheckoutKey) || request.CheckoutKey.Length > 100)
            throw new ArgumentException("Checkout key is required", nameof(request));
        var existing = await db
            .OrderTables.Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.CheckoutKey == request.CheckoutKey, ct);
        if (existing is not null)
        {
            var sameItems =
                existing.OrderItems.Count == request.Items.Count
                && existing.OrderItems.All(item =>
                    request.Items.Any(line =>
                        line.ProductId == item.ProductId && line.Quantity == item.Quantity
                    )
                );
            if (
                existing.BuyerId != buyerId
                || existing.AddressId != request.AddressId
                || !string.Equals(
                    existing.CouponCode,
                    string.IsNullOrWhiteSpace(request.CouponCode)
                        ? null
                        : request.CouponCode.Trim(),
                    StringComparison.OrdinalIgnoreCase
                )
                || !sameItems
            )
                throw new InvalidOperationException("Checkout key belongs to a different cart");
            if (immediate)
                await ValidateCapturedReplayAsync(existing, capturedAttempt!, ct);
            return existing;
        }

        IDbContextTransaction? tx =
            db.Database.IsRelational() && db.Database.CurrentTransaction is null
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                : null;
        try
        {
            // Serialize reservations before Serializable reads take shared stock/range locks.
            // This prevents two carts from deadlocking while upgrading the same coupon's lock.
            await PromotionLifecycle.LockReservationsAsync(db, ct);
            existing = await db
                .OrderTables.Include(x => x.OrderItems)
                .FirstOrDefaultAsync(x => x.CheckoutKey == request.CheckoutKey, ct);
            if (existing == null)
                await EnsureBuyerNotBannedAsync(buyerId, ct);
            if (existing != null)
            {
                if (
                    existing.BuyerId != buyerId
                    || existing.AddressId != request.AddressId
                    || !string.Equals(
                        existing.CouponCode,
                        string.IsNullOrWhiteSpace(request.CouponCode)
                            ? null
                            : request.CouponCode.Trim(),
                        StringComparison.OrdinalIgnoreCase
                    )
                    || existing.OrderItems.Count != request.Items.Count
                    || existing.OrderItems.Any(item =>
                        !request.Items.Any(line =>
                            line.ProductId == item.ProductId && line.Quantity == item.Quantity
                        )
                    )
                )
                    throw new InvalidOperationException("Checkout key belongs to a different cart");
                if (immediate)
                    await ValidateCapturedReplayAsync(existing, capturedAttempt!, ct);
                return existing;
            }

            var (quote, address, products, coupon) = await ValidateAndQuoteAsync(
                buyerId,
                request,
                ct,
                true,
                skipInventory: immediate
            );
            if (!immediate && products.Any(p => p.ImmediatePaymentRequired))
                throw new InvalidOperationException("Immediate Payment Required: use Buy It Now.");
            if (immediate && (products.Length != 1 || !products[0].ImmediatePaymentRequired))
                throw new InvalidOperationException("Product payment configuration has changed.");
            // Keep the finance gate until the new order commits so a debt update cannot race checkout.
            await (
                finance
                ?? new G4.Infrastructure.Finance.SellerFinanceService(
                    db,
                    config ?? new ConfigurationBuilder().Build()
                )
            ).ValidateNewSaleAsync(products[0].SellerId!.Value, ct);
            if (
                request.ExpectedTotal.HasValue && request.ExpectedTotal != quote.Total
                || request.PricingFingerprint != null
                    && request.PricingFingerprint != quote.PricingFingerprint
            )
                throw new InvalidOperationException(
                    "Giá hoặc khuyến mãi đã thay đổi. Hãy tính lại trước khi đặt hàng."
                );
            var pickup = await PickupAsync(products[0].SellerId!.Value, ct);
            var now = DateTime.UtcNow;
            var order = new OrderTable
            {
                ImmediatePaymentInProgress = immediate,
                BuyerId = address.UserId,
                SellerId = products[0].SellerId,
                AddressId = address.Id,
                DeliveryStateSnapshot = address.State,
                DeliveryCountrySnapshot = address.Country,
                DeliveryNameSnapshot = address.FullName,
                DeliveryStreetSnapshot = address.Street,
                DeliveryCitySnapshot = address.City,
                DeliveryPostalCodeSnapshot = address.PostalCode,
                DeliveryPhoneSnapshot = address.Phone,
                PickupStateSnapshot = pickup.State,
                PickupCountrySnapshot = pickup.Country,
                AddressSnapshot = string.Join(
                    ", ",
                    new[]
                    {
                        address.FullName,
                        address.Street,
                        address.City,
                        address.State,
                        address.Country,
                    }.Where(x => !string.IsNullOrWhiteSpace(x))
                ),
                PickupAddressSnapshot = string.Join(
                    ", ",
                    new[]
                    {
                        pickup.FullName,
                        pickup.Street,
                        pickup.City,
                        pickup.State,
                        pickup.Country,
                    }.Where(x => !string.IsNullOrWhiteSpace(x))
                ),
                TotalWeightKg = quote.TotalWeightKg,
                OrderDate = now,
                UpdatedAt = now,
                // Immediate orders are inserted already Paid, so they have no payment deadline.
                PaymentExpiresAt = immediate ? null : now.AddMinutes(PaymentHoldMinutes),
                Subtotal = quote.Subtotal,
                DiscountAmount = quote.Discount,
                ShippingFee = quote.Shipping,
                TotalPrice = quote.Total,
                Currency = "USD",
                CouponCode = string.IsNullOrWhiteSpace(request.CouponCode)
                    ? null
                    : request.CouponCode.Trim().ToUpperInvariant(),
                PricingSchemaVersion = coupon == null ? 1 : 0,
                ShippingBase = quote.ShippingBase,
                ShippingDiscount = quote.ShippingDiscount,
                SellerGoodsDiscount = quote.SellerDiscount,
                PlatformSubsidy = quote.PlatformSubsidy,
                SellerGrossSnapshot = quote.SellerGross,
                PricingFingerprint = quote.PricingFingerprint,
                CheckoutKey = request.CheckoutKey,
                Status = immediate ? "Paid" : "AwaitingPayment",
            };
            foreach (var line in request.Items)
            {
                var product = products.Single(p => p.Id == line.ProductId);
                var stock = await db.Inventories.SingleAsync(
                    i => i.ProductId == line.ProductId,
                    ct
                );
                if (!immediate && stock.Quantity < line.Quantity)
                    throw new InvalidOperationException("Insufficient stock");
                if (!immediate && db.Database.IsRelational())
                {
                    var quantity = line.Quantity;
                    var changed = await db.Inventories
                        .Where(i => i.Id == stock.Id && i.Quantity >= quantity)
                        .ExecuteUpdateAsync(update => update
                            .SetProperty(i => i.Quantity, i => i.Quantity - quantity)
                            .SetProperty(i => i.LastUpdated, DateTime.UtcNow), ct);
                    if (changed != 1)
                        throw new InvalidOperationException("Out of Stock");
                    await db.Entry(stock).ReloadAsync(ct);
                }
                else if (!immediate)
                {
                    stock.Quantity -= line.Quantity;
                    stock.LastUpdated = DateTime.UtcNow;
                }
                order.OrderItems.Add(
                    new OrderItem
                    {
                        ProductId = product.Id,
                        Quantity = line.Quantity,
                        UnitPrice = product.Price,
                        ProductTitleSnapshot = product.Title,
                        UnitWeightKgSnapshot = product.WeightKg,
                        SellerIdSnapshot = product.SellerId,
                        SellerDiscountSnapshot =
                            quote
                                .Lines.FirstOrDefault(x => x.ProductId == product.Id)
                                ?.SellerDiscount
                            ?? 0,
                        PlatformDiscountSnapshot =
                            quote
                                .Lines.FirstOrDefault(x => x.ProductId == product.Id)
                                ?.PlatformDiscount
                            ?? 0,
                    }
                );
            }

            db.OrderTables.Add(order);
            await db.SaveChangesAsync(ct);
            // The identity is now available; keep this alert in the existing checkout transaction.
            if (!immediate)
            {
                db.CommerceChanges.Add(
                    new CommerceChange
                    {
                        Topic = "orders",
                        OrderId = order.Id,
                        BuyerId = order.BuyerId,
                        SellerId = order.SellerId,
                        CreatedAt = now,
                        NotificationTitle = "Đơn hàng mới",
                        NotificationMessage = $"Đơn #{order.Id} đang chờ thanh toán.",
                    }
                );
            }
            foreach (var applied in quote.Promotions)
            {
                db.OrderPromotionSnapshots.Add(
                    new OrderPromotionSnapshot
                    {
                        OrderId = order.Id,
                        PromotionId = applied.Id,
                        Name = applied.Name,
                        Type = applied.Type,
                        FundingSource = applied.FundingSource,
                        Code = applied.Code,
                        Version = applied.Version,
                        Amount = applied.Amount,
                        AllocationJson = JsonSerializer.Serialize(applied.Lines),
                    }
                );
                db.PromotionUsages.Add(
                    new PromotionUsage
                    {
                        OrderId = order.Id,
                        PromotionId = applied.Id,
                        BuyerId = buyerId,
                        Amount = applied.Amount,
                        ReservedAt = now,
                    }
                );
            }

            if (immediate)
            {
                var payment = new Payment
                {
                    OrderId = order.Id,
                    UserId = buyerId,
                    Amount = quote.Total,
                    Method = "Card",
                    Status = "Succeeded",
                    ProviderTransactionId = capturedAttempt!.CaptureId,
                    IdempotencyKey = "immediate-" + capturedAttempt.CheckoutId.ToString("N"),
                    CreatedAt = now,
                    UpdatedAt = now,
                    PaidAt = now,
                };
                db.Payments.Add(payment);
                await PromotionLifecycle.ConsumeAsync(db, order.Id, ct);
                foreach (var usage in db.ChangeTracker.Entries<PromotionUsage>()
                    .Where(x => x.Entity.OrderId == order.Id))
                {
                    usage.Entity.State = "Consumed";
                    usage.Entity.ConsumedAt = now;
                }
                await QueueEmailAsync(order, "PaymentSucceeded", "Payment succeeded", "", ct);
                await db.SaveChangesAsync(ct);
                if (finance != null)
                {
                    await finance.ValidateMonthlyLimitAsync(order.Id, ct);
                    await finance.RecordSuccessfulPaymentAsync(order.Id, payment.Id, ct);
                }
            }

            if (!immediate && quote.Total == 0)
            {
                var zero = new Payment
                {
                    OrderId = order.Id,
                    UserId = buyerId,
                    Amount = 0,
                    Method = "Promotion",
                    Status = "Succeeded",
                    ProviderTransactionId = $"PROMOTION-{order.Id}",
                    IdempotencyKey = $"promotion-paid-{order.Id}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    PaidAt = now,
                };
                db.Payments.Add(zero);
                order.Status = "Paid";
                using var diagnostic = IntegrationContext.ForOrder(order.Id);
                await using var internalPayment = new IntegrationCallRecorder(logs).Start(
                    "Promotion",
                    "Payment",
                    "Internal"
                );
                await internalPayment.CompleteAsync(
                    "Succeeded",
                    providerReference: zero.ProviderTransactionId,
                    ct: ct
                );
                foreach (
                    var usage in db
                        .ChangeTracker.Entries<PromotionUsage>()
                        .Where(x => x.Entity.OrderId == order.Id)
                )
                {
                    usage.Entity.State = "Consumed";
                    usage.Entity.ConsumedAt = now;
                }

                await QueueEmailAsync(
                    order,
                    "PaymentSucceeded",
                    "Thanh toán bằng khuyến mãi",
                    $"Đơn hàng #{order.Id} được thanh toán bằng khuyến mãi.",
                    ct
                );
                await db.SaveChangesAsync(ct);
                if (finance != null)
                {
                    await finance.ValidateMonthlyLimitAsync(order.Id, ct);
                    await finance.RecordSuccessfulPaymentAsync(order.Id, zero.Id, ct);
                }
            }

            await db.SaveChangesAsync(ct);
            if (tx is not null)
                await tx.CommitAsync(ct);
            return order;
        }
        finally
        {
            if (tx is not null)
                await tx.DisposeAsync();
        }
    }

    private async Task ValidateCapturedReplayAsync(
        OrderTable order, ImmediatePaymentAttempt attempt, CancellationToken ct
    )
    {
        if (order.Status != "Paid" || !await db.Payments.AnyAsync(p => p.OrderId == order.Id
            && p.Status == "Succeeded" && p.ProviderTransactionId == attempt.CaptureId, ct))
            throw new InvalidOperationException("Checkout key belongs to another payment workflow.");
    }

    public async Task<Payment> PayCardAsync(
        int orderId,
        string number,
        string expiry,
        string idempotencyKey,
        CancellationToken ct = default
    )
    {
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new ArgumentException("Payment key is required", nameof(idempotencyKey));
        var existing = await db.Payments.FirstOrDefaultAsync(
            p => p.IdempotencyKey == idempotencyKey,
            ct
        );
        if (existing is not null)
        {
            if (existing.OrderId != orderId || existing.Method != "Card")
                throw new InvalidOperationException("Payment key belongs to another attempt");
            if (existing.Status == "Succeeded" && finance is not null)
                await finance.RecordSuccessfulPaymentAsync(orderId, existing.Id, ct);
            if (existing.Status == "Succeeded")
                await PromotionLifecycle.ConsumeAsync(db, orderId, ct);
            await db.SaveChangesAsync(ct);
            if (tx != null)
                await tx.CommitAsync(ct);
            return existing;
        }

        var order =
            await db.OrderTables.FindAsync(new object[] { orderId }, ct)
            ?? throw new KeyNotFoundException("Order not found");
        if (db.Database.IsRelational())
            await db.Entry(order).ReloadAsync(ct);
        ValidateAwaitingPayment(order);
        if (
            await db.Payments.AnyAsync(
                x =>
                    x.OrderId == orderId
                    && ((x.Method == "PayPal"
                        && (x.Status == "Verifying" || x.PayPalCheckoutId != null && x.Status == "Pending"))
                        || (x.Method == "Klarna"
                            && (x.Status == "Pending" || x.Status == "Verifying" || x.Status == "Authorized"))),
                ct
            )
        )
            throw new InvalidOperationException(
                "PayPal đang xác minh thanh toán. Hãy chờ kết quả."
            );
        if (finance is not null)
            await finance.ValidateMonthlyLimitAsync(orderId, ct);
        var outcome = CardPaymentSimulator.Outcome(number, expiry);
        var payment = new Payment
        {
            OrderId = orderId,
            UserId = order.BuyerId,
            Amount = order.TotalPrice,
            Method = "Card",
            Status = outcome,
            IdempotencyKey = idempotencyKey,
            ProviderTransactionId =
                outcome == "Succeeded" ? "CARD-" + Guid.NewGuid().ToString("N") : null,
            ErrorCode = outcome == "Succeeded" ? null : outcome,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            PaidAt = outcome == "Succeeded" ? DateTime.UtcNow : null,
        };
        db.Payments.Add(payment);
        using var diagnostic = IntegrationContext.ForOrder(orderId);
        await using var cardAttempt = new IntegrationCallRecorder(logs).Start(
            "Card",
            "Payment",
            "Simulated"
        );
        await cardAttempt.CompleteAsync(
            outcome == "Succeeded" ? "Succeeded" : "Failed",
            providerReference: payment.ProviderTransactionId,
            errorCode: payment.ErrorCode,
            safeSummary: outcome == "Succeeded"
                ? null
                : "Thanh toán thẻ giả lập chưa được chấp nhận.",
            ct: ct
        );
        if (outcome == "Succeeded")
        {
            order.Status = OrderState.Next(order.Status!, "PaymentSucceeded");
            await PromotionLifecycle.ConsumeAsync(db, orderId, ct);
            await QueueEmailAsync(
                order,
                "PaymentSucceeded",
                "Thanh toán thành công",
                $"Đơn hàng #{order.Id} đã được thanh toán thành công.",
                ct
            );
        }

        if (outcome != "Succeeded")
        {
            await db.SaveChangesAsync(ct); // Assign the attempt ID inside the current order transaction.
            await new BusinessNotificationService(db, config).PaymentFailedAsync(
                order,
                payment,
                ct
            );
        }

        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (outcome == "Succeeded" && finance is not null)
            await finance.RecordSuccessfulPaymentAsync(orderId, payment.Id, ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return payment;
    }

    public async Task<int> ExpirePendingAsync(CancellationToken ct = default)
    {
        // Unpaid Item flow: after the hold deadline the seller decides, so the worker must
        // not cancel the order (and must not skip the strike) when this switch is off.
        if (!AutoExpireUnpaid)
            return 0;
        var expired = await db
            .OrderTables.Include(o => o.OrderItems)
            .Where(o =>
                o.Status == "AwaitingPayment"
                && o.PaymentExpiresAt < DateTime.UtcNow
                && !db.Payments.Any(p =>
                    p.OrderId == o.Id && (p.Method == "PayPal" && p.Status == "Verifying" || p.Method == "Klarna" && (p.Status == "Pending" || p.Status == "Verifying" || p.Status == "Authorized"))
                )
            )
            .ToListAsync(ct);
        foreach (var order in expired)
        {
            await using var tx = await PromotionLifecycle.LockOrderAsync(db, order.Id, ct);
            if (db.Database.IsRelational())
                await db.Entry(order).ReloadAsync(ct);
            if (
                order.Status != "AwaitingPayment"
                || await db.Payments.AnyAsync(
                    x => x.OrderId == order.Id && (x.Method == "PayPal" && x.Status == "Verifying" || x.Method == "Klarna" && (x.Status == "Pending" || x.Status == "Verifying" || x.Status == "Authorized")),
                    ct
                )
            )
                continue;
            order.Status = OrderState.Next(order.Status!, "Expire");
            order.UpdatedAt = DateTime.UtcNow;
            await RestoreStockAsync(order, ct);
            await PromotionLifecycle.ReleaseAsync(db, order.Id, ct);
            await db.SaveChangesAsync(ct);
            if (tx != null)
                await tx.CommitAsync(ct);
        }

        await db.SaveChangesAsync(ct);
        return expired.Count;
    }

    public async Task<int> CloseDeliveredOutsideReturnWindowAsync(CancellationToken ct = default)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var legacyCutoff = now.AddSeconds(
            -G4.Infrastructure.Returns.ReturnWindowPolicy.LegacySeconds
        );
        var ids = await db
            .OrderTables.AsNoTracking()
            .Where(o =>
                o.Status == "Delivered"
                && (
                    o.ReturnWindowClosesAt <= now
                    || o.ReturnWindowClosesAt == null
                        && db.ShippingInfos.Any(s =>
                            s.OrderId == o.Id
                            && s.Direction == "Outbound"
                            && s.DeliveredAt <= legacyCutoff
                        )
                )
            )
            .OrderBy(o => o.Id)
            .Select(o => o.Id)
            .ToListAsync(ct);
        var count = 0;
        foreach (var id in ids)
        {
            await using var tx = await PromotionLifecycle.LockOrderAsync(db, id, ct);
            var order = db.Database.IsSqlServer()
                ? await db
                    .OrderTables.FromSqlInterpolated(
                        $"SELECT * FROM [OrderTable] WITH (UPDLOCK,HOLDLOCK) WHERE [id]={id}"
                    )
                    .SingleAsync(ct)
                : await db.OrderTables.SingleAsync(o => o.Id == id, ct);
            if (db.Database.IsRelational())
                await db.Entry(order).ReloadAsync(ct);
            var delivered = await db
                .ShippingInfos.AsNoTracking()
                .Where(s => s.OrderId == id && s.Direction == "Outbound")
                .Select(s => s.DeliveredAt)
                .SingleOrDefaultAsync(ct);
            // Rejected returns retain appeal rights. RT2 will own their eventual closing deadline.
            if (
                order.Status != "Delivered"
                || delivered == null
                || G4.Infrastructure.Returns.ReturnWindowPolicy.IsOpen(order, delivered, now)
                || await db.ReturnRequests.AnyAsync(
                    r => r.OrderId == id && r.Status != "Closed",
                    ct
                )
                || await db.Disputes.AnyAsync(d => d.OrderId == id && d.IsOpen, ct)
            )
            {
                if (tx != null)
                    await tx.CommitAsync(ct);
                continue;
            }
            order.Status = OrderState.Next(order.Status!, "Close");
            order.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            if (tx != null)
                await tx.CommitAsync(ct);
            count++;
        }
        return count;
    }

    public async Task<OrderTable> CancelUnpaidAsync(int orderId, CancellationToken ct = default)
    {
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        var order = await db
            .OrderTables.Include(o => o.OrderItems)
            .SingleAsync(o => o.Id == orderId, ct);
        if (db.Database.IsRelational())
            await db.Entry(order).ReloadAsync(ct);
        if (order.Status == "Cancelled")
        {
            if (tx != null)
                await tx.CommitAsync(ct);
            return order;
        }
        if (
            await db.Payments.AnyAsync(
                x => x.OrderId == orderId && (x.Method == "PayPal" && x.Status == "Verifying" || x.Method == "Klarna" && (x.Status == "Pending" || x.Status == "Verifying" || x.Status == "Authorized")),
                ct
            )
        )
            throw new InvalidOperationException(
                "PayPal đang xác minh thanh toán. Hãy chờ trước khi hủy."
            );
        order.Status = OrderState.Next(order.Status!, "Cancel");
        order.UpdatedAt = DateTime.UtcNow;
        await RestoreStockAsync(order, ct);
        await PromotionLifecycle.ReleaseAsync(db, orderId, ct);
        await new BusinessNotificationService(db, config).CancellationAsync(order, true, ct);
        await db.SaveChangesAsync(ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return order;
    }

    public async Task<OrderTable> SellerCancelUnpaidAsync(
        int orderId,
        int sellerId,
        string? reason,
        CancellationToken ct = default
    )
    {
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        var order = await db
            .OrderTables.Include(o => o.OrderItems)
            .SingleAsync(o => o.Id == orderId, ct);
        if (db.Database.IsRelational())
            await db.Entry(order).ReloadAsync(ct);
        if (order.SellerId != sellerId)
            throw new InvalidOperationException("Đơn hàng không thuộc người bán này.");
        // A retry after success returns the stored result without a second strike or restock.
        if (order.Status == "Cancelled" && order.CancelReasonCode == UnpaidItemRules.CancelReasonCode)
        {
            if (tx != null)
                await tx.CommitAsync(ct);
            return order;
        }
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        if (order.Status != "AwaitingPayment")
            throw new InvalidOperationException("Chỉ hủy được đơn đang chờ thanh toán.");
        if (!UnpaidItemRules.CanSellerCancel(order.Status, order.PaymentExpiresAt, now))
            throw new InvalidOperationException(
                "Chưa hết thời hạn thanh toán nên chưa thể hủy đơn."
            );
        if (
            await db.Payments.AnyAsync(
                x =>
                    x.OrderId == orderId
                    && (x.Status == "Verifying" || x.Status == "Succeeded"),
                ct
            )
        )
            throw new InvalidOperationException(
                "Đơn đang có thanh toán chờ xác minh hoặc đã thanh toán. Chưa thể hủy."
            );
        var note = string.IsNullOrWhiteSpace(reason)
            ? "Người mua không thanh toán trong thời hạn giữ hàng."
            : reason.Trim();
        if (note.Length > 500)
            note = note[..500];
        order.Status = OrderState.Next(order.Status!, "Cancel");
        order.CancelReasonCode = UnpaidItemRules.CancelReasonCode;
        order.CancelDecisionReason = note;
        order.UpdatedAt = now;
        await RestoreStockAsync(order, ct);
        await PromotionLifecycle.ReleaseAsync(db, orderId, ct);
        if (order.BuyerId is int buyerId)
        {
            db.UnpaidItemStrikes.Add(
                new UnpaidItemStrike
                {
                    BuyerId = buyerId,
                    SellerId = sellerId,
                    OrderId = order.Id,
                    Reason = note,
                    CreatedAt = now,
                }
            );
            await db.SaveChangesAsync(ct);
            await ApplyBanIfNeededAsync(buyerId, order.Id, now, ct);
        }
        await new BusinessNotificationService(db, config).CancellationAsync(order, true, ct);
        db.CommerceChanges.Add(
            new CommerceChange
            {
                Topic = "orders",
                OrderId = order.Id,
                BuyerId = order.BuyerId,
                SellerId = order.SellerId,
                CreatedAt = now,
                NotificationTitle = "Đơn bị hủy do chưa thanh toán",
                NotificationMessage =
                    $"Đơn #{order.Id} đã bị hủy vì quá hạn thanh toán. Người mua bị cộng 1 điểm phạt.",
            }
        );
        await db.SaveChangesAsync(ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return order;
    }

    private async Task ApplyBanIfNeededAsync(
        int buyerId,
        int orderId,
        DateTime now,
        CancellationToken ct
    )
    {
        var windowStart = now.AddMinutes(-StrikeWindowMinutes);
        var strikes = await db.UnpaidItemStrikes.CountAsync(
            s => s.BuyerId == buyerId && s.CreatedAt >= windowStart,
            ct
        );
        if (!UnpaidItemRules.ShouldBan(strikes, StrikeLimit))
            return;
        var buyer = await db.Users.SingleAsync(u => u.Id == buyerId, ct);
        if (buyer.BannedAt != null)
            return;
        buyer.BannedAt = now;
        buyer.BanReason =
            $"Bị khóa vĩnh viễn: {strikes} điểm phạt Unpaid Item liên tiếp (đơn #{orderId}).";
        db.CommerceChanges.Add(
            new CommerceChange
            {
                Topic = "orders",
                OrderId = orderId,
                BuyerId = buyerId,
                CreatedAt = now,
                NotificationTitle = "Tài khoản bị khóa",
                NotificationMessage = "Tài khoản bị khóa vĩnh viễn do 2 điểm phạt chưa thanh toán liên tiếp.",
            }
        );
    }

    private async Task EnsureBuyerNotBannedAsync(int buyerId, CancellationToken ct)
    {
        var banned = await db
            .Users.AsNoTracking()
            .Where(u => u.Id == buyerId)
            .Select(u => u.BannedAt)
            .SingleOrDefaultAsync(ct);
        if (banned != null)
            throw new InvalidOperationException(
                "Tài khoản đã bị khóa vĩnh viễn do nhiều lần không thanh toán đơn hàng."
            );
    }

    private int PaymentHoldMinutes =>
        UnpaidItemRules.NormalizeHoldMinutes(config?.GetValue<int?>("Checkout:PaymentHoldMinutes"));

    private bool AutoExpireUnpaid => config?.GetValue<bool?>("Checkout:AutoExpireUnpaid") ?? true;

    private int StrikeLimit =>
        UnpaidItemRules.NormalizeStrikeLimit(config?.GetValue<int?>("Checkout:UnpaidStrikeLimit"));

    private int StrikeWindowMinutes =>
        UnpaidItemRules.NormalizeStrikeWindowMinutes(
            config?.GetValue<int?>("Checkout:UnpaidStrikeWindowMinutes")
        );

    public async Task QueueEmailAsync(
        OrderTable order,
        string eventType,
        string subject,
        string body,
        CancellationToken ct = default
    )
    {
        await new NotificationService(db, config).EnqueueAsync(order, eventType, ct);
    }

    private async Task<(
        PriceQuote Quote,
        Address Address,
        Product[] Products,
        Coupon? Coupon
    )> ValidateAndQuoteAsync(
        int buyerId,
        CheckoutRequest request,
        CancellationToken ct,
        bool locked = false,
        bool skipInventory = false
    )
    {
        // Decision 11/11/2026: no limit on distinct products per order; quantity stays 1-5.
        if (
            request.Items is null
            || request.Items.Count < 1
            || request.Items.Any(i => i.Quantity is < 1 or > 5)
            || request.Items.Select(i => i.ProductId).Distinct().Count() != request.Items.Count
        )
            throw new ArgumentException(
                "Đơn hàng cần ít nhất 1 sản phẩm khác nhau, mỗi sản phẩm có số lượng từ 1 đến 5."
            );
        var address =
            await db.Addresses.AsNoTracking().SingleOrDefaultAsync(
                a => a.Id == request.AddressId && a.UserId == buyerId,
                ct
            )
            ?? throw new ArgumentException(
                "Chưa có địa chỉ giao hàng hợp lệ. Hãy thêm hoặc chọn địa chỉ trước khi thanh toán."
            );
        var ids = request.Items.Select(i => i.ProductId).ToArray();
        var products = await db
            .Products.AsNoTracking().Where(p => ids.Contains(p.Id) && p.Price != null
                && p.IsAuction != true && p.IsListed)
            .ToArrayAsync(ct);
        if (
            products.Length != ids.Length
            || products.Select(p => p.SellerId).Distinct().Count() != 1
        )
            throw new ArgumentException("All products must exist and belong to one seller");
        if (products.Any(p => p.WeightKg is null or <= 0))
            throw new InvalidOperationException(
                "Người bán cần khai báo khối lượng sản phẩm trước khi checkout."
            );
        var pickup = await PickupAsync(
            products[0].SellerId
                ?? throw new InvalidOperationException("Sản phẩm chưa có người bán."),
            ct
        );
        foreach (var line in request.Items.Where(_ => !skipInventory))
        {
            var stock = await db.Inventories.AsNoTracking().SingleOrDefaultAsync(
                i => i.ProductId == line.ProductId,
                ct
            );
            if ((stock?.Quantity ?? 0) < line.Quantity)
                throw new InvalidOperationException("Insufficient stock");
        }

        Coupon? coupon = null;
        var baseQuote = OrderPricing.Calculate(
            request.Items.Select(i => new PriceLine(
                products.Single(p => p.Id == i.ProductId).Price!.Value,
                i.Quantity,
                products.Single(p => p.Id == i.ProductId).WeightKg!.Value
            )),
            0,
            ShippingPricing.Zone(
                pickup.State,
                pickup.Country,
                address.State,
                address.Country
            )
        );
        var normalized = string.IsNullOrWhiteSpace(request.CouponCode)
            ? null
            : request.CouponCode.Trim().ToUpperInvariant();
        // The legacy fallback keeps existing unmigrated test fixtures valid. SQL migration maps all existing coupons.
        if (
            db.Database.IsRelational()
            || normalized == null
            || await db.Promotions.AnyAsync(x => x.Code == normalized, ct)
        )
        {
            var promoted = await new PromotionQuoteService(db).CalculateAsync(
                buyerId,
                products[0].SellerId!.Value,
                request
                    .Items.Select(i =>
                    {
                        var p = products.Single(p => p.Id == i.ProductId);
                        return new PromotionPriceLine(
                            p.Id,
                            p.CategoryId,
                            p.Price!.Value,
                            i.Quantity,
                            p.WeightKg!.Value
                        );
                    })
                    .ToArray(),
                baseQuote.Shipping,
                address.Id,
                JsonSerializer.Serialize(
                    new
                    {
                        delivery = new
                        {
                            address.Id,
                            address.FullName,
                            address.Phone,
                            address.Street,
                            address.City,
                            address.State,
                            address.Country,
                        },
                        pickup = new
                        {
                            pickup.Id,
                            pickup.FullName,
                            pickup.Phone,
                            pickup.Street,
                            pickup.City,
                            pickup.State,
                            pickup.Country,
                        },
                    }
                ),
                normalized,
                locked,
                ct
            );
            return (promoted, address, products, null);
        }

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            coupon =
                await db.Coupons.SingleOrDefaultAsync(
                    c => c.Code == request.CouponCode && c.ProductId == null,
                    ct
                ) ?? throw new ArgumentException("Coupon not found");
            var now = DateTime.UtcNow;
            if (
                coupon.StartDate > now
                || coupon.EndDate < now
                || coupon.DiscountPercent is null or < 0 or > 20
            )
                throw new ArgumentException("Coupon is inactive");
            if (coupon.MaxUsage is int max)
            {
                var used = await db.OrderTables.CountAsync(
                    o =>
                        o.CouponCode == coupon.Code
                        && o.Status != "Cancelled"
                        && o.Status != "Expired",
                    ct
                );
                if (used >= max)
                    throw new ArgumentException("Coupon usage limit reached");
            }
        }

        var quote = OrderPricing.Calculate(
            request.Items.Select(i => new PriceLine(
                products.Single(p => p.Id == i.ProductId).Price!.Value,
                i.Quantity,
                products.Single(p => p.Id == i.ProductId).WeightKg!.Value
            )),
            coupon?.DiscountPercent ?? 0m,
            ShippingPricing.Zone(
                pickup.State,
                pickup.Country,
                address.State,
                address.Country
            )
        );
        return (quote, address, products, coupon);
    }

    private async Task<Address> PickupAsync(int sellerId, CancellationToken ct) =>
        await db
            .Addresses.Where(a => a.UserId == sellerId && a.IsDefault == true)
            .OrderBy(a => a.Id)
            .FirstOrDefaultAsync(ct)
        ?? throw new InvalidOperationException(
            "Người bán cần khai báo địa chỉ lấy hàng trước khi checkout."
        );

    private static void ValidateAwaitingPayment(OrderTable order)
    {
        if (order.Status != "AwaitingPayment")
            throw new InvalidOperationException("Order is not awaiting payment");
        if (order.PaymentExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("Payment window expired");
    }

    private async Task RestoreStockAsync(OrderTable order, CancellationToken ct)
    {
        foreach (var item in order.OrderItems.OrderBy(x => x.ProductId))
        {
            if (db.Database.IsRelational())
            {
                var quantity = item.Quantity ?? 0;
                var now = DateTime.UtcNow;
                var updated = await db
                    .Inventories.Where(x => x.ProductId == item.ProductId)
                    .ExecuteUpdateAsync(
                        update =>
                            update
                                .SetProperty(x => x.Quantity, x => (x.Quantity ?? 0) + quantity)
                                .SetProperty(x => x.LastUpdated, now),
                        ct
                    );
                if (updated != 1)
                    throw new InvalidOperationException("Không tìm thấy tồn kho để hoàn lại hàng.");
                continue;
            }

            var stock = await db.Inventories.SingleAsync(i => i.ProductId == item.ProductId, ct);
            stock.Quantity += item.Quantity ?? 0;
            stock.LastUpdated = DateTime.UtcNow;
        }
    }
}
