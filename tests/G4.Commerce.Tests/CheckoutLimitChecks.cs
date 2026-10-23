using System.Text.Json;
using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;

internal static class CheckoutLimitChecks
{
    public static async Task RunAsync()
    {
        await SellerCountAsync();
        await QuantityAsync();
        Console.WriteLine(
            "Checkout limits: at most two sellers, quantities 1-5 across cart/quote/create, rejected requests preserve data, legacy retries passed"
        );
    }

    private static async Task SellerCountAsync()
    {
        await using var db = await FixtureAsync();
        var carts = new BuyerCartService(db, new CheckoutService(db));
        var cart = await carts.GetAsync(1);
        cart = await carts.SaveAsync(1, new(cart.Version, [new(1, 1), new(2, 5)]));
        var before = await SnapshotAsync(db);
        await RejectAsync(() =>
            carts.SaveAsync(1, new(cart.Version, [new(1, 1), new(2, 5), new(3, 1)]))
        );
        Check(
            await SnapshotAsync(db) == before,
            "rejecting a third seller must preserve persisted cart, version, stock and checkout data"
        );

        // A cart saved before this fix may already contain three sellers. Validate
        // all requested lines, including unchanged ones, while allowing its repair.
        var stored = await db.BuyerCarts.SingleAsync();
        stored.ItemsJson = JsonSerializer.Serialize(
            new CartLine[] { new(1, 1), new(2, 1), new(3, 1) }
        );
        await db.SaveChangesAsync();
        before = await SnapshotAsync(db);
        await RejectAsync(() =>
            carts.SaveAsync(1, new(stored.Version, [new(1, 1), new(2, 1), new(3, 1)]))
        );
        Check(
            await SnapshotAsync(db) == before,
            "an unchanged legacy three-seller cart must also be rejected without writes"
        );
        cart = await carts.SaveAsync(1, new(stored.Version, [new(1, 1), new(2, 1)]));
        Check(
            cart.Items.Select(x => x.SellerId).Distinct().Count() == 2,
            "legacy cart can be reduced to two sellers"
        );

        // Existing unavailable items must remain removable after the new check.
        (await db.Products.FindAsync(1))!.Price = null;
        (await db.Inventories.FindAsync(1))!.Quantity = 0;
        await db.SaveChangesAsync();
        cart = await carts.SaveAsync(1, new(cart.Version, [new(2, 1)]));
        Check(
            cart.Items.Count == 1 && cart.Items[0].SellerId == 3,
            "unavailable item can be removed, leaving one seller"
        );
        cart = await carts.SaveAsync(1, new(cart.Version, []));
        Check(cart.Items.Count == 0, "empty cart remains valid");
    }

    private static async Task QuantityAsync()
    {
        await using var db = await FixtureAsync();
        var checkout = new CheckoutService(db);
        var carts = new BuyerCartService(db, checkout);
        var cart = await carts.GetAsync(1);
        cart = await carts.SaveAsync(1, new(cart.Version, [new(1, 1), new(2, 5)]));
        var before = await SnapshotAsync(db);
        foreach (var quantity in new[] { -1, 0, 6, int.MaxValue })
        {
            await RejectAsync(() => carts.SaveAsync(1, new(cart.Version, [new(1, quantity)])));
            var invalid = new CheckoutRequest(1, [new(1, quantity)], null, "quantity-limit-retry");
            await RejectAsync(() => checkout.QuoteAsync(1, invalid));
            await RejectAsync(() => checkout.CreateOrderAsync(1, invalid));
            Check(
                await SnapshotAsync(db) == before,
                $"quantity {quantity} must fail before any cart, stock or checkout writes"
            );
        }

        foreach (var quantity in new[] { 1, 5 })
        {
            var request = new CheckoutRequest(
                1,
                [new(1, quantity)],
                null,
                quantity == 5 ? "quantity-limit-retry" : "quantity-one"
            );
            var quote = await checkout.QuoteAsync(1, request);
            var order = await checkout.CreateOrderAsync(
                1,
                request with
                {
                    ExpectedTotal = quote.Total,
                    PricingFingerprint = quote.PricingFingerprint,
                }
            );
            Check(
                order.OrderItems.Single().Quantity == quantity,
                $"quantity boundary {quantity} remains purchasable"
            );
            var after = await SnapshotAsync(db);
            Check(
                (await checkout.CreateOrderAsync(1, request)).Id == order.Id,
                "same checkout key returns the existing order"
            );
            Check(
                await SnapshotAsync(db) == after,
                "valid checkout retry cannot reserve stock twice"
            );
        }
        Check(
            (await db.Inventories.FindAsync(1))!.Quantity == 14,
            "only the two valid orders reserve 1 + 5 items"
        );

        // Existing orders retain their original snapshots/idempotency even if an
        // old order exceeds the new-order limit. New keys must still be rejected.
        var legacy = new OrderTable
        {
            BuyerId = 1,
            SellerId = 2,
            AddressId = 1,
            CheckoutKey = "legacy-six",
            Status = "AwaitingPayment",
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 1,
                    Quantity = 6,
                    UnitPrice = 10m,
                },
            ],
        };
        db.OrderTables.Add(legacy);
        await db.SaveChangesAsync();
        before = await SnapshotAsync(db);
        var legacyRequest = new CheckoutRequest(1, [new(1, 6)], null, "legacy-six");
        Check(
            (await checkout.CreateOrderAsync(1, legacyRequest)).Id == legacy.Id,
            "historical order retry keeps its original snapshot"
        );
        await RejectAsync(() =>
            checkout.CreateOrderAsync(1, legacyRequest with { CheckoutKey = "new-six" })
        );
        Check(
            await SnapshotAsync(db) == before,
            "legacy retry and rejected new order must not alter old orders or stock"
        );
    }

    private static async Task<ApplicationDbContext> FixtureAsync()
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        db.Users.Add(new User { Id = 1, Role = "buyer" });
        db.Addresses.Add(
            new Address
            {
                Id = 1,
                UserId = 1,
                State = "Hanoi",
                Country = "Vietnam",
            }
        );
        for (var id = 1; id <= 3; id++)
        {
            db.Users.Add(new User { Id = id + 1, Role = "seller" });
            db.Addresses.Add(
                new Address
                {
                    Id = id + 1,
                    UserId = id + 1,
                    IsDefault = true,
                    State = "Hanoi",
                    Country = "Vietnam",
                }
            );
            db.Products.Add(
                new Product
                {
                    Id = id,
                    SellerId = id + 1,
                    Title = "Item " + id,
                    Price = 10m,
                    WeightKg = .5m,
                }
            );
            db.Inventories.Add(
                new Inventory
                {
                    Id = id,
                    ProductId = id,
                    Quantity = 20,
                }
            );
        }
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<string> SnapshotAsync(ApplicationDbContext db) =>
        JsonSerializer.Serialize(
            new
            {
                carts = await db
                    .BuyerCarts.AsNoTracking()
                    .OrderBy(x => x.BuyerId)
                    .Select(x => new { x.ItemsJson, x.Version })
                    .ToListAsync(),
                stock = await db
                    .Inventories.AsNoTracking()
                    .OrderBy(x => x.Id)
                    .Select(x => new { x.Id, x.Quantity })
                    .ToListAsync(),
                orders = await db
                    .OrderTables.AsNoTracking()
                    .OrderBy(x => x.Id)
                    .Select(x => new
                    {
                        x.Id,
                        x.TotalPrice,
                        x.CheckoutKey,
                    })
                    .ToListAsync(),
                items = await db
                    .OrderItems.AsNoTracking()
                    .OrderBy(x => x.Id)
                    .Select(x => new { x.Id, x.Quantity })
                    .ToListAsync(),
                batches = await db.CheckoutBatches.CountAsync(),
                payments = await db.Payments.CountAsync(),
                usages = await db.PromotionUsages.CountAsync(),
                ledger = await db.FinancialTransactions.CountAsync(),
                notifications = await db.NotificationOutbox.CountAsync(),
            }
        );

    private static async Task RejectAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new Exception(
            "Invalid seller count or quantity was accepted instead of raising ArgumentException"
        );
    }

    private static void Check(bool condition, string message) =>
        ReturnAutomationChecks.Check(condition, message);
}
