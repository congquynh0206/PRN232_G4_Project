using System.Data;
using System.Text.Json;
using G4.Contracts.Checkout;
using G4.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace G4.Infrastructure.Checkout;
public sealed class BuyerCartService(ApplicationDbContext db, ICheckoutService checkout) : IBuyerCartService
{
    private static string SerializeLines(IEnumerable<CartLine> items) => JsonSerializer.Serialize(items.OrderBy(x => x.ProductId));
    private async Task<IDbContextTransaction?> BeginAsync(CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return null;
        var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await PromotionLifecycle.LockReservationsAsync(db, ct);
            return tx;
        }
        catch
        {
            await tx.DisposeAsync();
            throw;
        }
    }

    private async Task<BuyerCart> CartAsync(int buyerId, CancellationToken ct)
    {
        var cart = await db.BuyerCarts.SingleOrDefaultAsync(x => x.BuyerId == buyerId, ct);
        if (cart != null)
            return cart;
        cart = new BuyerCart
        {
            BuyerId = buyerId
        };
        db.BuyerCarts.Add(cart);
        return cart;
    }

    private static void CheckVersion(BuyerCart cart, Guid version)
    {
        if (cart.Version != version)
            throw new InvalidOperationException("Giỏ hàng đã thay đổi ở phiên khác. Hãy tải lại giỏ hàng.");
    }

    private async Task<BuyerCartResponse> ViewAsync(BuyerCart cart, CancellationToken ct)
    {
        var lines = JsonSerializer.Deserialize<CartLine[]>(cart.ItemsJson)!;
        var ids = lines.Select(x => x.ProductId).ToArray();
        var products = await db.Products.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var stock = await db.Inventories.AsNoTracking().Where(x => ids.Contains(x.ProductId ?? 0)).ToDictionaryAsync(x => x.ProductId!.Value, ct);
        var sellerIds = products.Values.Select(x => x.SellerId).ToArray();
        var names = await db.Users.AsNoTracking().Where(x => sellerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Username, ct);
        return new(cart.Version, lines.Select(line =>
        {
            products.TryGetValue(line.ProductId, out var p);
            return new CartItem(
                line.ProductId,
                p?.Title ?? "Sản phẩm không còn tồn tại",
                p?.Price,
                p?.WeightKg,
                p?.Images,
                line.Quantity,
                stock.GetValueOrDefault(line.ProductId)?.Quantity ?? 0,
                p?.SellerId,
                p?.SellerId is int seller ? names.GetValueOrDefault(seller) ?? $"Seller #{seller}" : "Không có người bán"
            );
        })
            .ToArray());
    }

    public async Task<BuyerCartResponse> GetAsync(int buyerId, CancellationToken ct = default)
    {
        await using var tx = await BeginAsync(ct);
        var cart = await CartAsync(buyerId, ct);
        await db.SaveChangesAsync(ct);
        var result = await ViewAsync(cart, ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return result;
    }

    public async Task<BuyerCartResponse> SaveAsync(int buyerId, SaveCartRequest request, CancellationToken ct = default)
    {
        if (request.Items is null || request.Items.Count > 5 || request.Items.Any(x => x.Quantity < 1 || x.Quantity > 5) || request.Items.Select(x => x.ProductId).Distinct().Count() != request.Items.Count)
            throw new ArgumentException("Giỏ hàng tối đa 5 sản phẩm, mỗi sản phẩm có số lượng từ 1 đến 5.");
        await using var tx = await BeginAsync(ct);
        var cart = await CartAsync(buyerId, ct);
        CheckVersion(cart, request.Version);
        // Keep unavailable existing lines removable; validate only additions or quantity increases.
        var old = JsonSerializer.Deserialize<CartLine[]>(cart.ItemsJson)!.ToDictionary(x => x.ProductId, x => x.Quantity);
        foreach (var line in request.Items.Where(x => x.Quantity > old.GetValueOrDefault(x.ProductId)))
        {
            if (!await db.Products.AnyAsync(
                x => x.Id == line.ProductId && x.SellerId != null && x.Price != null && x.IsAuction != true && x.WeightKg > 0,
                ct
            ) || !await db.Inventories.AnyAsync(x => x.ProductId == line.ProductId && x.Quantity >= line.Quantity, ct))
                throw new InvalidOperationException("Sản phẩm không còn đủ tồn kho hoặc chưa đủ thông tin để mua.");
        }

        cart.ItemsJson = SerializeLines(request.Items);
        cart.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        var result = await ViewAsync(cart, ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return result;
    }

    public async Task<BuyerCartResponse> RandomAsync(int buyerId, RandomCartRequest request, CancellationToken ct = default)
    {
        if (request.Count is < 2 or > 5)
            throw new ArgumentException("Chọn từ 2 đến 5 sản phẩm để mua từ hai seller.");
        await using var tx = await BeginAsync(ct);
        var cart = await CartAsync(buyerId, ct);
        CheckVersion(cart, request.Version);
        var now = DateTime.UtcNow;
        var products = await db.Products.AsNoTracking().Where(x => x.SellerId != null && x.Price != null && x.IsAuction != true && x.WeightKg > 0 && db.Addresses.Any(a => a.UserId == x.SellerId && a.IsDefault == true) && db.Inventories.Any(i => i.ProductId == x.Id && i.Quantity > 0))
            .Where(p => !db.SellerAccounts.Any(a => a.SellerId == p.SellerId &&
                (a.Status != "Active" || a.NegativeBalance > 0 && a.DebtDueAt <= now)))
            .ToListAsync(ct);
        var sellers = products.GroupBy(x => x.SellerId!.Value).OrderBy(_ => Random.Shared.Next()).ToArray();
        var pairs = (
            from a in sellers
            from b in sellers
            where a.Key < b.Key && a.Count() + b.Count() >= request.Count
            select (a, b)).ToArray();
        if (pairs.Length == 0)
            throw new InvalidOperationException("Cần hai seller có địa chỉ lấy hàng và đủ sản phẩm còn hàng để tạo giỏ.");
        var pair = pairs[Random.Shared.Next(pairs.Length)];
        var first = pair.a.OrderBy(_ => Random.Shared.Next()).First();
        var second = pair.b.OrderBy(_ => Random.Shared.Next()).First();
        var chosen = new[]
        {
            first,
            second
        }.Concat(pair.a.Concat(pair.b).Where(x => x.Id != first.Id && x.Id != second.Id).OrderBy(_ => Random.Shared.Next())
            .Take(request.Count - 2))
            .ToArray();
        var ids = chosen.Select(x => x.Id).ToArray();
        var inventory = await db.Inventories.Where(x => ids.Contains(x.ProductId ?? 0)).ToDictionaryAsync(x => x.ProductId!.Value, ct);
        cart.ItemsJson = SerializeLines(chosen.Select(x => new CartLine(x.Id, Random.Shared.Next(1, Math.Min(3, inventory[x.Id].Quantity!.Value) + 1))));
        cart.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        var result = await ViewAsync(cart, ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return result;
    }

    private async Task<(BuyerCart Cart, BuyerCartResponse View)> ValidateAsync(int buyerId, CartCheckoutRequest request, CancellationToken ct)
    {
        if (request.Sellers is null || request.Sellers.Count is < 1 or > 2 || request.Sellers.Select(x => x.SellerId).Distinct().Count() != request.Sellers.Count)
            throw new ArgumentException("Checkout cần một hoặc hai nhóm seller khác nhau.");
        var cart = await CartAsync(buyerId, ct);
        CheckVersion(cart, request.CartVersion);
        var view = await ViewAsync(cart, ct);
        if (view.Items.Count == 0 || view.Items.Any(x => x.SellerId == null) || !view.Items.Select(x => x.SellerId!.Value).Distinct().Order().SequenceEqual(request.Sellers.Select(x => x.SellerId).Order()))
            throw new ArgumentException("Nhóm seller không khớp giỏ hàng hiện tại.");
        return (cart, view);
    }

    private static CheckoutRequest OrderRequest(
        CartCheckoutRequest request,
        BuyerCartResponse cart,
        SellerCheckoutRequest seller
    ) => new(
        request.AddressId,
        cart.Items.Where(x => x.SellerId == seller.SellerId).Select(x => new CartLine(x.ProductId, x.Quantity))
        .ToArray(),
        seller.CouponCode,
        $"batch:{request.CheckoutKey}:{seller.SellerId}",
        seller.ExpectedTotal,
        seller.PricingFingerprint
    );
    public async Task<CartQuoteResponse> QuoteAsync(int buyerId, CartCheckoutRequest request, CancellationToken ct = default)
    {
        var(_, view) = await ValidateAsync(buyerId, request, ct);
        var quotes = new List<SellerCartQuote>();
        foreach (var seller in request.Sellers.OrderBy(x => x.SellerId))
            quotes.Add(new(
                seller.SellerId,
                view.Items.First(x => x.SellerId == seller.SellerId).SellerName,
                await checkout.QuoteAsync(buyerId, OrderRequest(request, view, seller), ct)
            ));
        return new(
            quotes,
            quotes.Sum(x => x.Quote.Subtotal),
            quotes.Sum(x => x.Quote.Discount),
            quotes.Sum(x => x.Quote.Shipping),
            quotes.Sum(x => x.Quote.Total),
            quotes.Sum(x => x.Quote.TotalWeightKg)
        );
    }

    public async Task<IReadOnlyList<CartOrderResponse>> CheckoutAsync(int buyerId, CartCheckoutRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.CheckoutKey) || request.CheckoutKey.Length > 60 || request.Sellers is null || request.Sellers.Any(x => x.ExpectedTotal is null || string.IsNullOrWhiteSpace(x.PricingFingerprint)))
            throw new ArgumentException("Cần khóa checkout và báo giá đã xác nhận cho từng seller.");
        var canonical = JsonSerializer.Serialize(request with { Sellers = request.Sellers.OrderBy(x => x.SellerId).Select(x => x with { CouponCode = string.IsNullOrWhiteSpace(x.CouponCode) ? null : x.CouponCode.Trim().ToUpperInvariant() })
            .ToArray() });
        await using var tx = await BeginAsync(ct);
        var previous = await db.CheckoutBatches.SingleOrDefaultAsync(x => x.Key == request.CheckoutKey, ct);
        if (previous != null)
        {
            if (previous.BuyerId != buyerId || previous.RequestJson != canonical)
                throw new InvalidOperationException("Khóa checkout thuộc về một giỏ hàng khác.");
            var ids = JsonSerializer.Deserialize<int[]>(previous.OrderIdsJson)!;
            var orders = await db.OrderTables.Where(x => ids.Contains(x.Id)).OrderBy(x => x.SellerId).ToListAsync(ct);
            if (tx != null)
                await tx.CommitAsync(ct);
            return orders.Select(Response).ToArray();
        }

        var(cart, view) = await ValidateAsync(buyerId, request, ct);
        // Validate every group before any stock reservation; validate again under the same SQL transaction when creating.
        foreach (var seller in request.Sellers)
        {
            var quote = await checkout.QuoteAsync(buyerId, OrderRequest(request, view, seller), ct);
            if (quote.Total != seller.ExpectedTotal || quote.PricingFingerprint != seller.PricingFingerprint)
                throw new InvalidOperationException("Giá hoặc khuyến mãi đã thay đổi. Hãy cập nhật báo giá trước khi đặt hàng.");
        }

        var created = new List<OrderTable>();
        foreach (var seller in request.Sellers.OrderBy(x => x.SellerId))
            created.Add(await checkout.CreateOrderAsync(buyerId, OrderRequest(request, view, seller), ct));
        db.CheckoutBatches.Add(new CheckoutBatch { Key = request.CheckoutKey, BuyerId = buyerId, RequestJson = canonical, OrderIdsJson = JsonSerializer.Serialize(created.Select(x => x.Id)) });
        cart.ItemsJson = "[]";
        cart.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return created.Select(Response).ToArray();
    }

    private static CartOrderResponse Response(OrderTable order) => new(order.Id, order.SellerId, order.Status, order.TotalPrice, order.PaymentExpiresAt);
}
