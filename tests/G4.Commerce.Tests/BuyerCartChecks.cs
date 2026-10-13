using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;

internal static class BuyerCartChecks
{
    public static async Task RunAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        db.Users.AddRange(new User { Id = 1, Role = "buyer" }, new User { Id = 4, Role = "buyer" },
            new User { Id = 2, Role = "seller", Username = "Shop A" }, new User { Id = 3, Role = "seller", Username = "Shop B" });
        db.Addresses.AddRange(new Address { Id = 1, UserId = 1, State = "Hanoi", Country = "Vietnam" },
            new Address { Id = 2, UserId = 2, State = "Hanoi", Country = "Vietnam", IsDefault = true },
            new Address { Id = 3, UserId = 3, State = "HCM", Country = "Vietnam", IsDefault = true });
        for (var id = 1; id <= 6; id++)
        {
            db.Products.Add(new Product { Id = id, SellerId = id <= 3 ? 2 : 3, Price = 10, WeightKg = .5m, Title = "Item " + id });
            db.Inventories.Add(new Inventory { Id = id, ProductId = id, Quantity = 20 });
        }
        await db.SaveChangesAsync();
        var service = new BuyerCartService(db, new CheckoutService(db));
        var cart = await service.GetAsync(1);
        for (var count = 2; count <= 5; count++)
        {
            cart = await service.RandomAsync(1, new(cart.Version, count));
            Check(cart.Items.Count == count && cart.Items.Select(x => x.SellerId).Distinct().Count() == 2, "random must include exactly two sellers");
        }
        await Reject(() => service.RandomAsync(1, new(cart.Version, 1)), "one product cannot contain two sellers");
        Check(db.Inventories.All(x => x.Quantity == 20), "cart does not reserve stock");
        await using (var next = new ApplicationDbContext(options))
        {
            var saved = await new BuyerCartService(next, new CheckoutService(next)).GetAsync(1);
            Check(saved.Version == cart.Version && saved.Items.Count == cart.Items.Count, "cart persists across sessions");
        }
        Check((await service.GetAsync(4)).Items.Count == 0, "buyer carts are isolated");
        await Reject(() => service.SaveAsync(4, new(cart.Version, [new(1, 1)])), "another buyer cannot use the version");
        var oldVersion = cart.Version;
        cart = await service.SaveAsync(1, new(cart.Version, [new(1, 2), new(4, 1)]));
        await Reject(() => service.SaveAsync(1, new(oldVersion, [])), "stale edits cannot overwrite cart");
        await Reject(() => service.SaveAsync(1, new(cart.Version, [new(1, 0)])), "reject zero quantity");
        await Reject(() => service.SaveAsync(1, new(cart.Version, [new(1, 1), new(1, 1)])), "reject duplicate product");
        var request = new CartCheckoutRequest(1, cart.Version, [new(2), new(3)], "cart-batch");
        var quote = await service.QuoteAsync(1, request);
        Check(quote.Sellers.Count == 2 && quote.Shipping == 7 && quote.Total == 37, "each seller has independent shipping");
        var confirmed = request with { Sellers = quote.Sellers.Select(x => new SellerCheckoutRequest(x.SellerId, null, x.Quote.Total, x.Quote.PricingFingerprint)).ToArray() };
        await Reject(() => service.CheckoutAsync(1, confirmed with { Sellers = confirmed.Sellers.Select(x => x with { ExpectedTotal = x.ExpectedTotal + 1 }).ToArray() }), "reject stale totals");
        db.Products.Local.Single(x => x.Id == 4).Price = 11;
        await db.SaveChangesAsync();
        await Reject(() => service.CheckoutAsync(1, confirmed), "reject changed price on second seller");
        Check(!await db.OrderTables.AnyAsync() && db.Inventories.All(x => x.Quantity == 20), "validation failure leaves no orders or stock reservations");
        db.Products.Local.Single(x => x.Id == 4).Price = 10;
        await db.SaveChangesAsync();
        var orders = await service.CheckoutAsync(1, confirmed);
        Check(orders.Count == 2 && orders.Select(x => x.SellerId).Distinct().Count() == 2, "checkout splits one order per seller");
        Check((await service.GetAsync(1)).Items.Count == 0, "checkout clears purchased cart");
        Check((await db.Inventories.FindAsync(1))!.Quantity == 18 && (await db.Inventories.FindAsync(4))!.Quantity == 19, "stock reserved per seller");
        var repeated = await service.CheckoutAsync(1, confirmed);
        Check(repeated.Select(x => x.Id).SequenceEqual(orders.Select(x => x.Id)) && await db.OrderTables.CountAsync() == 2, "retry returns same orders after cart cleared");
        await Reject(() => service.CheckoutAsync(4, confirmed), "batch keys cannot be reused by other buyer");
        await Reject(() => service.CheckoutAsync(1, confirmed with { AddressId = 2 }), "batch keys cannot be reused for another cart");
        var oneSeller = await service.SaveAsync(1, new((await service.GetAsync(1)).Version, [new(1, 1)]));
        var singleRequest = new CartCheckoutRequest(1, oneSeller.Version, [new(2)], "single-cart-batch");
        var singleQuote = await service.QuoteAsync(1, singleRequest);
        var singleOrders = await service.CheckoutAsync(1, singleRequest with { Sellers = [new(2, null, singleQuote.Total, singleQuote.Sellers[0].Quote.PricingFingerprint)] });
        Check(singleOrders.Count == 1, "removing one seller allows single-seller checkout");
        Console.WriteLine("Buyer cart persistence, isolation, two-seller random, shipping, stale quote and idempotent split checkout checks passed");
    }

    private static void Check(bool condition, string message) => ReturnAutomationChecks.Check(condition, message);
    private static async Task Reject(Func<Task> work, string message)
    {
        try { await work(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; }
        throw new Exception(message);
    }
}
