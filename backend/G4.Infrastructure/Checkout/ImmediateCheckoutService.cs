using System.Data;
using System.Text.Json;
using G4.Contracts.Checkout;
using G4.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Checkout;

public sealed class ImmediateCheckoutService(ApplicationDbContext db, CheckoutService checkout)
{
    public async Task<object> StartAsync(
        int buyerId,
        ImmediateCheckoutInput input,
        CancellationToken ct = default
    )
    {
        var product = await db.Products.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == input.ProductId, ct);
        if (product == null || !product.IsListed || !product.ImmediatePaymentRequired
            || product.IsAuction == true || product.SellerId == buyerId)
            throw new InvalidOperationException("Product is unavailable for Immediate Payment.");
        var session = new ImmediateCheckout
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
        };
        var request = new CheckoutRequest(
            input.AddressId, [new(input.ProductId, input.Quantity)], input.CouponCode,
            "immediate-" + session.Id.ToString("N")
        );
        var quote = await checkout.QuoteAsync(buyerId, request, ct);
        session.QuoteJson = JsonSerializer.Serialize(new
        {
            title = product.Title,
            quantity = input.Quantity,
            quote,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        session.RequestJson = JsonSerializer.Serialize(request with
        {
            ExpectedTotal = quote.Total,
            PricingFingerprint = quote.PricingFingerprint,
        });
        db.ImmediateCheckouts.Add(session);
        await db.SaveChangesAsync(ct);
        return new { session.Id, session.Status, session.ExpiresAt, product.Title, quote };
    }

    public async Task<object> GetAsync(int buyerId, Guid id, CancellationToken ct = default)
    {
        var session = await db.ImmediateCheckouts.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == id && s.BuyerId == buyerId, ct)
            ?? throw new KeyNotFoundException("Checkout not found");
        var attempt = await db.ImmediatePaymentAttempts.AsNoTracking()
            .Where(a => a.CheckoutId == id).OrderByDescending(a => a.Id)
            .Select(a => new { a.Status, a.ErrorCode, a.Key }).FirstOrDefaultAsync(ct);
        return new
        {
            session.Id,
            Status = session.Status == "Ready" && session.ExpiresAt <= DateTime.UtcNow
                ? "Expired" : session.Status,
            session.OrderId, session.ExpiresAt, attempt,
            snapshot = JsonSerializer.Deserialize<JsonElement>(session.QuoteJson),
        };
    }

    public async Task<ImmediatePaymentAttempt> PayCardAsync(
        int buyerId,
        Guid id,
        CardPaymentRequest input,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(input.Key) || input.Key.Length > 100)
            throw new ArgumentException("Payment key is required (maximum 100 characters).");
        // The existing card gateway is a pure local simulator. There is no external
        // authorization/capture to wait for or to compensate. The simulated charge,
        // inventory, order, promotion, settlement and attempt commit together.
        // Never call PayPal or another HTTP provider inside this transaction.
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        await PromotionLifecycle.LockReservationsAsync(db, ct);
        var session = await db.ImmediateCheckouts
            .SingleOrDefaultAsync(s => s.Id == id && s.BuyerId == buyerId, ct)
            ?? throw new KeyNotFoundException("Checkout not found");
        var previous = await db.ImmediatePaymentAttempts
            .SingleOrDefaultAsync(a => a.Key == input.Key, ct);
        if (previous != null && previous.CheckoutId != id)
            throw new InvalidOperationException("Payment key belongs to another checkout.");
        if (previous != null)
        {
            if (tx != null)
                await tx.CommitAsync(ct);
            return previous;
        }
        // A new key on an already-paid session also replays the original result.
        if (session.Status == "Succeeded")
        {
            var paid = await db.ImmediatePaymentAttempts
                .SingleAsync(a => a.CheckoutId == id && a.Status == "Succeeded", ct);
            if (tx != null)
                await tx.CommitAsync(ct);
            return paid;
        }
        if (session.ExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("Checkout expired. Request a new quote.");
        if (session.Status is "Pending" or "Verifying")
            throw new InvalidOperationException("Payment is being verified. Check status first.");
        var request = JsonSerializer.Deserialize<CheckoutRequest>(session.RequestJson)!;
        var productId = request.Items.Single().ProductId;
        var product = await db.Products.AsNoTracking()
            .SingleAsync(p => p.Id == productId, ct);
        if (!product.IsListed || !product.ImmediatePaymentRequired)
            throw new InvalidOperationException("Product payment configuration has changed.");
        // Quote again under the same lock used by normal checkout and seller edits.
        var quote = await checkout.QuoteAsync(buyerId, request, ct);
        if (quote.Total != request.ExpectedTotal
            || quote.PricingFingerprint != request.PricingFingerprint)
            throw new InvalidOperationException("Price changed. Review a new checkout quote.");
        var attempt = new ImmediatePaymentAttempt
        {
            CheckoutId = id,
            Key = input.Key,
            CreatedAt = DateTime.UtcNow,
        };
        db.ImmediatePaymentAttempts.Add(attempt);
        // Declines leave an auditable attempt, with no order or stock mutation.
        var outcome = CardPaymentSimulator.Outcome(input.Number, input.Expiry);
        if (outcome != "Succeeded")
        {
            attempt.Status = "Failed";
            attempt.ErrorCode = outcome;
        }
        else
        {
            var order = await checkout.CreateImmediateOrderAsync(buyerId, request, ct);
            if (order.Status != "Paid")
            {
                var payment = await checkout.PayCardAsync(
                    order.Id, input.Number, input.Expiry, "immediate-" + id.ToString("N"), ct
                );
                if (payment.Status != "Succeeded")
                    throw new InvalidOperationException("Card payment did not succeed.");
            }
            session.OrderId = attempt.OrderId = order.Id;
            session.Status = attempt.Status = "Succeeded";
        }
        await db.SaveChangesAsync(ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return attempt;
    }
}
