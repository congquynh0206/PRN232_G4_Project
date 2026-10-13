using G4.Domain.Entities;
using G4.Infrastructure.Promotions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

internal static class BuyerCartSqlChecks
{
    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("G4_CART_SQL_TEST_CONNECTION") ?? throw new Exception("QA connection required");
        if (new SqlConnectionStringBuilder(connection).InitialCatalog != "G4_MultiSeller_Test_20261008")
            throw new Exception("SQL cart checks require the dedicated QA database");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        int buyerId, addressId, sellerA, sellerB, productA, productB, couponId;
        CartCheckoutRequest request;
        int initialOrders, stockA, stockB;
        var key = Guid.NewGuid().ToString("N");
        await using (var db = new ApplicationDbContext(options))
        {
            var buyer = new User { Email = $"cart-{key}@example.test", Role = "buyer", Username = "Cart QA" };
            db.Users.Add(buyer); await db.SaveChangesAsync(); buyerId = buyer.Id;
            var address = new Address { UserId = buyerId, State = "Hanoi", Country = "Vietnam" };
            db.Addresses.Add(address); await db.SaveChangesAsync(); addressId = address.Id;
            sellerA = (await db.Users.SingleAsync(x => x.Email == "seller@example.test")).Id;
            sellerB = (await db.Users.SingleAsync(x => x.Email == "seller2@example.test")).Id;
            productA = await db.Products.Where(x => x.SellerId == sellerA).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
            productB = await db.Products.Where(x => x.SellerId == sellerB).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
            stockA = (await db.Inventories.SingleAsync(x => x.ProductId == productA)).Quantity!.Value;
            stockB = (await db.Inventories.SingleAsync(x => x.ProductId == productB)).Quantity!.Value;
            initialOrders = await db.OrderTables.CountAsync();
            var service = new BuyerCartService(db, new CheckoutService(db));
            var cart = await service.GetAsync(buyerId);
            cart = await service.RandomAsync(buyerId, new(cart.Version, 5));
            Check(cart.Items.Select(x => x.SellerId).Distinct().Count() == 2, "SQL random two sellers");
            cart = await service.SaveAsync(buyerId, new(cart.Version, [new(productA, 1), new(productB, 1)]));
            var admin = await db.Users.SingleAsync(x => x.Role == "admin");
            var coupon = await new PromotionManagementService(db).SaveAsync(admin.Id, true, new PromotionInput
            {
                Name = "Batch rollback QA", Type = "Coupon", Code = "BATCH" + key[..12].ToUpperInvariant(),
                Value = 1, MaxUsage = 1, StartAt = DateTime.UtcNow.AddHours(-1), EndAt = DateTime.UtcNow.AddDays(1)
            });
            couponId = coupon.Id;
            request = new(addressId, cart.Version, [new(sellerA, coupon.Code), new(sellerB, coupon.Code)], key);
            var quote = await service.QuoteAsync(buyerId, request);
            request = request with { Sellers = quote.Sellers.Select(x => new SellerCheckoutRequest(x.SellerId, coupon.Code, x.Quote.Total, x.Quote.PricingFingerprint)).ToArray() };
            try { await service.CheckoutAsync(buyerId, request); throw new Exception("coupon usage conflict accepted"); }
            catch (ArgumentException) { }
        }
        await using (var db = new ApplicationDbContext(options))
        {
            Check(await db.OrderTables.CountAsync() == initialOrders, "SQL rollback removes first seller order");
            Check(!await db.PromotionUsages.AnyAsync(x => x.PromotionId == couponId), "SQL rollback releases reservations");
            Check(!await db.CheckoutBatches.AnyAsync(x => x.Key == key), "SQL rollback removes batch");
            Check((await db.Inventories.SingleAsync(x => x.ProductId == productA)).Quantity == stockA &&
                (await db.Inventories.SingleAsync(x => x.ProductId == productB)).Quantity == stockB, "SQL rollback restores both stocks");
            var service = new BuyerCartService(db, new CheckoutService(db));
            Check((await service.GetAsync(buyerId)).Items.Count == 2, "SQL rollback keeps cart");
            request = request with { Sellers = [new(sellerA), new(sellerB)] };
            var quote = await service.QuoteAsync(buyerId, request);
            request = request with { Sellers = quote.Sellers.Select(x => new SellerCheckoutRequest(x.SellerId, null, x.Quote.Total, x.Quote.PricingFingerprint)).ToArray() };
        }
        async Task<IReadOnlyList<CartOrderResponse>> Checkout()
        {
            await using var db = new ApplicationDbContext(options);
            return await new BuyerCartService(db, new CheckoutService(db)).CheckoutAsync(buyerId, request);
        }
        var results = await Task.WhenAll(Checkout(), Checkout());
        Check(results[0].Select(x => x.Id).SequenceEqual(results[1].Select(x => x.Id)), "concurrent checkout returns same batch");
        await using (var db = new ApplicationDbContext(options))
        {
            Check(await db.OrderTables.CountAsync() == initialOrders + 2, "only two SQL orders created");
            Check((await db.Inventories.SingleAsync(x => x.ProductId == productA)).Quantity == stockA - 1 &&
                (await db.Inventories.SingleAsync(x => x.ProductId == productB)).Quantity == stockB - 1, "concurrent checkout reserves once");
            Check((await new BuyerCartService(db, new CheckoutService(db)).GetAsync(buyerId)).Items.Count == 0, "SQL checkout clears cart");
            var service = new CheckoutService(db);
            foreach (var order in results[0])
                Check((await service.PayCardAsync(order.Id, "4111111111111111", "12/30", key + "-pay-" + order.Id)).Status == "Succeeded", "pay each seller order");
        }
        Guid version;
        await using (var db = new ApplicationDbContext(options))
            version = (await new BuyerCartService(db, new CheckoutService(db)).GetAsync(buyerId)).Version;
        async Task<bool> Save(int product)
        {
            await using var db = new ApplicationDbContext(options);
            try { await new BuyerCartService(db, new CheckoutService(db)).SaveAsync(buyerId, new(version, [new(product, 1)])); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var saves = await Task.WhenAll(Save(productA), Save(productB));
        Check(saves.Count(x => x) == 1, "concurrent cart edits cannot overwrite one another");
        Console.WriteLine("Real SQL two-seller random, atomic rollback, concurrent checkout/edit and independent card payments passed");
    }

    private static void Check(bool condition, string message) => ReturnAutomationChecks.Check(condition, message);
}
