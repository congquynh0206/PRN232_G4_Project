using System.Data;
using System.Text.Json;
using G4.Contracts.Checkout;
using G4.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Checkout;

public sealed partial class ImmediateCheckoutService(
    ApplicationDbContext db,
    CheckoutService checkout,
    IImmediateCardGateway gateway
)
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
            .Select(a => new { a.Status, a.Stage, a.ErrorCode, a.Key })
            .FirstOrDefaultAsync(ct);
        return new
        {
            session.Id,
            Status = session.Status == "Ready" && session.ExpiresAt <= DateTime.UtcNow
                ? "Expired" : session.Status,
            session.OrderId, session.ExpiresAt, attempt,
            snapshot = JsonSerializer.Deserialize<JsonElement>(session.QuoteJson),
        };
    }

}
