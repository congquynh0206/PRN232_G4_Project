using System.Data;
using backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace backend.Checkout;

public sealed record CartLine(int ProductId, int Quantity);
public sealed record CheckoutRequest(int AddressId, IReadOnlyList<CartLine> Items, string? CouponCode, string CheckoutKey);

public sealed class MvpService(ApplicationDbContext db)
{
    private const string DemoBuyerEmail = "demo.buyer@example.test";
    private const string SellerState = "Hanoi";

    public async Task<IReadOnlyList<Product>> RandomProductsAsync(int count, CancellationToken ct = default)
    {
        if (count is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(count));
        var products = await db.Products.AsNoTracking()
            .Where(p => p.SellerId != null && p.IsAuction != true && p.Price != null)
            .Join(db.Inventories.Where(i => i.Quantity > 0), p => p.Id, i => i.ProductId,
                (p, i) => p)
            .ToListAsync(ct);
        var bySeller = products.GroupBy(p => p.SellerId).FirstOrDefault(g => g.Count() >= count);
        if (bySeller is null) throw new InvalidOperationException("Not enough in-stock products from one seller");
        return bySeller.OrderBy(_ => Random.Shared.Next()).Take(count).ToArray();
    }

    public async Task<PriceQuote> QuoteAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        var (quote, _, _, _) = await ValidateAndQuoteAsync(request, ct);
        return quote;
    }

    public async Task<OrderTable> CreateOrderAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.CheckoutKey) || request.CheckoutKey.Length > 100)
            throw new ArgumentException("Checkout key is required", nameof(request));
        var existing = await db.OrderTables.Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.CheckoutKey == request.CheckoutKey, ct);
        if (existing is not null)
        {
            var sameItems = existing.OrderItems.Count == request.Items.Count &&
                existing.OrderItems.All(item => request.Items.Any(line =>
                    line.ProductId == item.ProductId && line.Quantity == item.Quantity));
            if (existing.AddressId != request.AddressId ||
                !string.Equals(existing.CouponCode, request.CouponCode, StringComparison.OrdinalIgnoreCase) || !sameItems)
                throw new InvalidOperationException("Checkout key belongs to a different cart");
            return existing;
        }

        IDbContextTransaction? tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            var (quote, address, products, coupon) = await ValidateAndQuoteAsync(request, ct);
            var order = new OrderTable
            {
                BuyerId = address.UserId,
                SellerId = products[0].SellerId,
                AddressId = address.Id,
                AddressSnapshot = string.Join(", ", new[] { address.FullName, address.Street, address.City, address.State, address.Country }.Where(x => !string.IsNullOrWhiteSpace(x))),
                OrderDate = DateTime.UtcNow,
                PaymentExpiresAt = DateTime.UtcNow.AddMinutes(15),
                Subtotal = quote.Subtotal,
                DiscountAmount = quote.Discount,
                ShippingFee = quote.Shipping,
                TotalPrice = quote.Total,
                Currency = "USD",
                CouponCode = coupon?.Code,
                CheckoutKey = request.CheckoutKey,
                Status = "AwaitingPayment"
            };
            foreach (var line in request.Items)
            {
                var product = products.Single(p => p.Id == line.ProductId);
                var stock = await db.Inventories.SingleAsync(i => i.ProductId == line.ProductId, ct);
                if (stock.Quantity < line.Quantity) throw new InvalidOperationException("Insufficient stock");
                stock.Quantity -= line.Quantity;
                stock.LastUpdated = DateTime.UtcNow;
                order.OrderItems.Add(new OrderItem
                {
                    ProductId = product.Id,
                    Quantity = line.Quantity,
                    UnitPrice = product.Price,
                    ProductTitleSnapshot = product.Title,
                    SellerIdSnapshot = product.SellerId
                });
            }
            db.OrderTables.Add(order);
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return order;
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    public async Task<Payment> PayCardAsync(int orderId, string number, string expiry, string idempotencyKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new ArgumentException("Payment key is required", nameof(idempotencyKey));
        var existing = await db.Payments.FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null)
        {
            if (existing.OrderId != orderId || existing.Method != "Card")
                throw new InvalidOperationException("Payment key belongs to another attempt");
            return existing;
        }
        var order = await db.OrderTables.FindAsync(new object[] { orderId }, ct)
            ?? throw new KeyNotFoundException("Order not found");
        ValidateAwaitingPayment(order);
        var outcome = FakeCardProcessor.Outcome(number, expiry);
        var payment = new Payment
        {
            OrderId = orderId, UserId = order.BuyerId, Amount = order.TotalPrice,
            Method = "Card", Status = outcome, IdempotencyKey = idempotencyKey,
            ProviderTransactionId = outcome == "Succeeded" ? "CARD-" + Guid.NewGuid().ToString("N") : null,
            ErrorCode = outcome == "Succeeded" ? null : outcome,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            PaidAt = outcome == "Succeeded" ? DateTime.UtcNow : null
        };
        db.Payments.Add(payment);
        if (outcome == "Succeeded")
        {
            order.Status = OrderState.Next(order.Status!, "PaymentSucceeded");
            await QueueEmailAsync(order, "PaymentSucceeded", "Payment confirmed", $"Order {order.Id} was paid successfully.", ct);
        }
        await db.SaveChangesAsync(ct);
        return payment;
    }

    public async Task<int> ExpirePendingAsync(CancellationToken ct = default)
    {
        var expired = await db.OrderTables.Include(o => o.OrderItems)
            .Where(o => o.Status == "AwaitingPayment" && o.PaymentExpiresAt < DateTime.UtcNow &&
                !db.Payments.Any(p => p.OrderId == o.Id && p.Method == "PayPal" && p.Status == "Verifying"))
            .ToListAsync(ct);
        foreach (var order in expired)
        {
            order.Status = OrderState.Next(order.Status!, "Expire");
            await RestoreStockAsync(order, ct);
        }
        await db.SaveChangesAsync(ct);
        return expired.Count;
    }

    public async Task<int> CloseDeliveredOutsideReturnWindowAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var orders = await db.OrderTables.Where(o => o.Status == "Delivered" &&
            db.ShippingInfos.Any(s => s.OrderId == o.Id && s.Direction == "Outbound" && s.DeliveredAt < cutoff) &&
            !db.ReturnRequests.Any(r => r.OrderId == o.Id && r.Status != "Rejected"))
            .ToListAsync(ct);
        foreach (var order in orders) order.Status = OrderState.Next(order.Status!, "Close");
        await db.SaveChangesAsync(ct);
        return orders.Count;
    }

    public async Task<OrderTable> CancelUnpaidAsync(int orderId, CancellationToken ct = default)
    {
        var order = await db.OrderTables.Include(o => o.OrderItems).SingleAsync(o => o.Id == orderId, ct);
        order.Status = OrderState.Next(order.Status!, "Cancel");
        await RestoreStockAsync(order, ct);
        await db.SaveChangesAsync(ct);
        return order;
    }

    public async Task QueueEmailAsync(OrderTable order, string eventType, string subject, string body, CancellationToken ct = default)
    {
        if (await db.NotificationOutbox.AnyAsync(x => x.OrderId == order.Id && x.EventType == eventType, ct)) return;
        var recipient = await db.Users.Where(u => u.Id == order.BuyerId).Select(u => u.Email).SingleOrDefaultAsync(ct);
        db.NotificationOutbox.Add(new NotificationOutbox
        {
            OrderId = order.Id, EventType = eventType, Recipient = recipient ?? "buyer@example.test",
            Subject = subject, Body = body, CreatedAt = DateTime.UtcNow
        });
    }

    private async Task<(PriceQuote Quote, Address Address, Product[] Products, Coupon? Coupon)> ValidateAndQuoteAsync(
        CheckoutRequest request, CancellationToken ct)
    {
        if (request.Items.Count is < 1 or > 5 || request.Items.Any(i => i.Quantity <= 0) ||
            request.Items.Select(i => i.ProductId).Distinct().Count() != request.Items.Count)
            throw new ArgumentException("Cart must contain 1-5 distinct products with positive quantities");
        var buyerId = await db.Users.Where(u => u.Email == DemoBuyerEmail).Select(u => u.Id).SingleOrDefaultAsync(ct);
        var address = await db.Addresses.SingleOrDefaultAsync(a => a.Id == request.AddressId && a.UserId == buyerId, ct)
            ?? throw new ArgumentException("Buyer address not found");
        var ids = request.Items.Select(i => i.ProductId).ToArray();
        var products = await db.Products.Where(p => ids.Contains(p.Id) && p.Price != null && p.IsAuction != true).ToArrayAsync(ct);
        if (products.Length != ids.Length || products.Select(p => p.SellerId).Distinct().Count() != 1)
            throw new ArgumentException("All products must exist and belong to one seller");
        foreach (var line in request.Items)
        {
            var stock = await db.Inventories.SingleOrDefaultAsync(i => i.ProductId == line.ProductId, ct);
            if (stock?.Quantity < line.Quantity || stock is null) throw new InvalidOperationException("Insufficient stock");
        }
        Coupon? coupon = null;
        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            coupon = await db.Coupons.SingleOrDefaultAsync(c => c.Code == request.CouponCode && c.ProductId == null, ct)
                ?? throw new ArgumentException("Coupon not found");
            var now = DateTime.UtcNow;
            if (coupon.StartDate > now || coupon.EndDate < now || coupon.DiscountPercent is null or < 0 or > 20)
                throw new ArgumentException("Coupon is inactive");
            if (coupon.MaxUsage is int max)
            {
                var used = await db.OrderTables.CountAsync(o => o.CouponCode == coupon.Code &&
                    o.Status != "Cancelled" && o.Status != "Expired", ct);
                if (used >= max) throw new ArgumentException("Coupon usage limit reached");
            }
        }
        var quote = OrderPricing.Calculate(request.Items.Select(i =>
            new PriceLine(products.Single(p => p.Id == i.ProductId).Price!.Value, i.Quantity)),
            coupon?.DiscountPercent ?? 0m,
            string.Equals(address.State, SellerState, StringComparison.OrdinalIgnoreCase));
        return (quote, address, products, coupon);
    }

    private static void ValidateAwaitingPayment(OrderTable order)
    {
        if (order.Status != "AwaitingPayment") throw new InvalidOperationException("Order is not awaiting payment");
        if (order.PaymentExpiresAt <= DateTime.UtcNow) throw new InvalidOperationException("Payment window expired");
    }

    private async Task RestoreStockAsync(OrderTable order, CancellationToken ct)
    {
        foreach (var item in order.OrderItems)
        {
            var stock = await db.Inventories.SingleAsync(i => i.ProductId == item.ProductId, ct);
            stock.Quantity += item.Quantity ?? 0;
            stock.LastUpdated = DateTime.UtcNow;
        }
    }
}
