using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

// Buy It Now order that is created first and paid later: hold deadline, seller cancel after the
// deadline, Unpaid Item Strike, stock restore and permanent ban after two strikes.
internal static class UnpaidItemChecks
{
    public static async Task RunAsync()
    {
        Rules();
        await HoldDeadlineAsync();
        await BuyItNowAsync();
        await SellerCancelAsync();
        await TwoStrikesBanAsync();
        await StrikeWindowAsync();
        Console.WriteLine(
            "Unpaid items: Buy It Now creates one seller order as unpaid, configurable hold deadline, seller cancel only after deadline, one strike per order, stock restored once, two strikes lock the buyer, old strikes expire from the window"
        );
    }

    private static void Rules()
    {
        var now = new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);
        Check(UnpaidItemRules.NormalizeHoldMinutes(null) == 15, "legacy default hold is 15 minutes");
        Check(UnpaidItemRules.NormalizeHoldMinutes(5) == 5, "demo hold is 5 minutes");
        Check(UnpaidItemRules.NormalizeHoldMinutes(5760) == 5760, "production hold is 4 days");
        Check(UnpaidItemRules.NormalizeHoldMinutes(0) == 15, "invalid hold falls back");
        Check(
            !UnpaidItemRules.CanSellerCancel("AwaitingPayment", now.AddSeconds(1), now),
            "seller cancel stays closed before the deadline"
        );
        Check(
            UnpaidItemRules.CanSellerCancel("AwaitingPayment", now, now),
            "seller cancel opens at the deadline"
        );
        Check(
            !UnpaidItemRules.CanSellerCancel("Paid", now.AddDays(-1), now),
            "a paid order can never be cancelled as unpaid"
        );
        Check(!UnpaidItemRules.ShouldBan(1, 2), "one strike does not lock the buyer");
        Check(UnpaidItemRules.ShouldBan(2, 2), "two strikes lock the buyer");
    }

    private static async Task HoldDeadlineAsync()
    {
        await using var db = await FixtureAsync();
        var before = DateTime.UtcNow;
        var demo = await new CheckoutService(db, config: Config(("Checkout:PaymentHoldMinutes", "5")))
            .CreateOrderAsync(1, Request("hold-demo"));
        var minutes = (demo.PaymentExpiresAt!.Value - before).TotalMinutes;
        Check(minutes is > 4.9 and < 5.2, $"demo hold must be about 5 minutes, got {minutes}");
        var legacy = await new CheckoutService(db).CreateOrderAsync(1, Request("hold-legacy"));
        minutes = (legacy.PaymentExpiresAt!.Value - before).TotalMinutes;
        Check(minutes is > 14.9 and < 15.2, $"missing config keeps the 15 minute hold, got {minutes}");
    }

    private static async Task BuyItNowAsync()
    {
        await using var db = await FixtureAsync();
        var checkout = new CheckoutService(db, config: Config(("Checkout:PaymentHoldMinutes", "5")));
        var carts = new BuyerCartService(db, checkout);
        var cart = await carts.GetAsync(1);
        cart = await carts.SaveAsync(1, new(cart.Version, [new(1, 2), new(2, 1)]));
        var quote = await checkout.QuoteAsync(1, Request("bn-quote", quantity: 2));
        var request = new BuyItNowRequest(
            1,
            cart.Version,
            2,
            "bn-key",
            null,
            quote.Total,
            quote.PricingFingerprint
        );

        await RejectAsync(() => carts.BuyItNowAsync(1, request with { PricingFingerprint = "stale" }));
        Check(await db.OrderTables.CountAsync() == 0, "a stale quote must not create an order");

        var order = await carts.BuyItNowAsync(1, request);
        Check(
            order.Status == "AwaitingPayment" && order.SellerId == 2,
            "Buy It Now creates the seller order as unpaid (AwaitingPayment)"
        );
        Check(order.PaymentExpiresAt != null, "the unpaid order has a payment deadline");
        Check((await db.Inventories.FindAsync(1))!.Quantity == 18, "the item is held at once");
        Check(
            (await db.Inventories.FindAsync(2))!.Quantity == 20,
            "the other seller's stock is untouched"
        );
        var after = await carts.GetAsync(1);
        Check(
            after.Items.Count == 1 && after.Items[0].SellerId == 3,
            "only the bought seller's lines leave the cart"
        );
        Check(after.Version != cart.Version, "the cart version changes after Buy It Now");

        var again = await carts.BuyItNowAsync(1, request);
        Check(
            again.Id == order.Id && await db.OrderTables.CountAsync() == 1,
            "retrying the same key returns the same order"
        );
        Check((await db.Inventories.FindAsync(1))!.Quantity == 18, "a retry cannot hold stock twice");
        await RejectAsync(() => carts.BuyItNowAsync(1, request with { CheckoutKey = "bn-key-2" }));
        Check(await db.OrderTables.CountAsync() == 1, "an old cart version cannot create a second order");
    }

    private static async Task SellerCancelAsync()
    {
        await using var db = await FixtureAsync();
        var checkout = new CheckoutService(db, config: Config(("Checkout:AutoExpireUnpaid", "false")));
        var order = await checkout.CreateOrderAsync(1, Request("cancel-1", quantity: 3));
        var stock = await db.Inventories.FindAsync(1);
        Check(stock!.Quantity == 17, "creating the order holds the stock");

        await RejectAsync(() => checkout.SellerCancelUnpaidAsync(order.Id, 2, null));
        Check(order.Status == "AwaitingPayment", "cancel before the deadline changes nothing");
        Check(await db.UnpaidItemStrikes.CountAsync() == 0, "no strike before the deadline");

        order.PaymentExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        Check(
            await checkout.ExpirePendingAsync() == 0 && order.Status == "AwaitingPayment",
            "with auto expire off the worker leaves the decision to the seller"
        );
        await RejectAsync(() => checkout.SellerCancelUnpaidAsync(order.Id, 3, null));
        Check(await db.UnpaidItemStrikes.CountAsync() == 0, "another seller cannot cancel the order");

        var result = await checkout.SellerCancelUnpaidAsync(order.Id, 2, "  Không thanh toán ");
        Check(result.Status == "Cancelled", "seller cancel after the deadline cancels the order");
        Check(
            result.CancelReasonCode == UnpaidItemRules.CancelReasonCode,
            "cancel reason code is UnpaidItem"
        );
        Check(result.CancelDecisionReason == "Không thanh toán", "reason is trimmed and stored");
        Check(stock.Quantity == 20, "stock is restored exactly once (stock = stock + quantity)");
        var strike = await db.UnpaidItemStrikes.SingleAsync();
        Check(strike.BuyerId == 1 && strike.OrderId == order.Id, "one strike is added to the buyer");
        Check((await db.Users.FindAsync(1))!.BannedAt == null, "one strike does not lock the buyer");

        await checkout.SellerCancelUnpaidAsync(order.Id, 2, null);
        Check(await db.UnpaidItemStrikes.CountAsync() == 1, "retry cannot add a second strike");
        Check(stock.Quantity == 20, "retry cannot restore stock twice");

        var paid = await checkout.CreateOrderAsync(1, Request("cancel-paid"));
        paid.PaymentExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        paid.Status = "Paid";
        await db.SaveChangesAsync();
        await RejectAsync(() => checkout.SellerCancelUnpaidAsync(paid.Id, 2, null));
        Check(
            await db.UnpaidItemStrikes.CountAsync() == 1,
            "a paid order is never cancelled or struck"
        );
    }

    private static async Task TwoStrikesBanAsync()
    {
        await using var db = await FixtureAsync();
        var checkout = new CheckoutService(db, config: Config(("Checkout:AutoExpireUnpaid", "false")));
        for (var i = 1; i <= 2; i++)
        {
            var order = await checkout.CreateOrderAsync(1, Request($"ban-{i}"));
            order.PaymentExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
            await checkout.SellerCancelUnpaidAsync(order.Id, 2, null);
            var banned = (await db.Users.FindAsync(1))!.BannedAt != null;
            Check(banned == (i == 2), $"strike {i}: locked state must be {i == 2}");
        }
        var user = await db.Users.FindAsync(1);
        Check(user!.BanReason != null, "the lock reason is stored");
        var snapshot = await db.OrderTables.CountAsync();
        await RejectAsync(() => checkout.CreateOrderAsync(1, Request("after-ban")));
        Check(
            await db.OrderTables.CountAsync() == snapshot,
            "a locked buyer cannot create another order"
        );
    }

    private static async Task StrikeWindowAsync()
    {
        await using var db = await FixtureAsync();
        var checkout = new CheckoutService(
            db,
            config: Config(
                ("Checkout:AutoExpireUnpaid", "false"),
                ("Checkout:UnpaidStrikeWindowMinutes", "60")
            )
        );
        var old = await checkout.CreateOrderAsync(1, Request("window-old"));
        db.UnpaidItemStrikes.Add(
            new UnpaidItemStrike
            {
                BuyerId = 1,
                SellerId = 2,
                OrderId = old.Id,
                Reason = "old",
                CreatedAt = DateTime.UtcNow.AddMinutes(-120),
            }
        );
        old.Status = "Cancelled";
        await db.SaveChangesAsync();
        var order = await checkout.CreateOrderAsync(1, Request("window-new"));
        order.PaymentExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        await checkout.SellerCancelUnpaidAsync(order.Id, 2, null);
        Check(
            (await db.Users.FindAsync(1))!.BannedAt == null,
            "a strike older than the window does not count as consecutive"
        );
    }

    private static CheckoutRequest Request(string key, int quantity = 1) =>
        new(1, [new(1, quantity)], null, key);

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(x => x.Key, x => (string?)x.Value))
            .Build();

    private static async Task<ApplicationDbContext> FixtureAsync()
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        db.Users.Add(new User { Id = 1, Role = "buyer", Email = "buyer@example.test" });
        db.Users.Add(new User { Id = 2, Role = "seller" });
        db.Users.Add(new User { Id = 3, Role = "seller" });
        db.Addresses.Add(
            new Address
            {
                Id = 1,
                UserId = 1,
                State = "Hanoi",
                Country = "Vietnam",
            }
        );
        db.Addresses.Add(
            new Address
            {
                Id = 2,
                UserId = 2,
                IsDefault = true,
                State = "Hanoi",
                Country = "Vietnam",
            }
        );
        db.Addresses.Add(
            new Address
            {
                Id = 3,
                UserId = 3,
                IsDefault = true,
                State = "Hanoi",
                Country = "Vietnam",
            }
        );
        db.Products.Add(
            new Product
            {
                Id = 2,
                SellerId = 3,
                Title = "Item 2",
                Price = 20m,
                WeightKg = .5m,
            }
        );
        db.Inventories.Add(
            new Inventory
            {
                Id = 2,
                ProductId = 2,
                Quantity = 20,
            }
        );
        db.Products.Add(
            new Product
            {
                Id = 1,
                SellerId = 2,
                Title = "Item 1",
                Price = 10m,
                WeightKg = .5m,
            }
        );
        db.Inventories.Add(
            new Inventory
            {
                Id = 1,
                ProductId = 1,
                Quantity = 20,
            }
        );
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task RejectAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new Exception("An invalid unpaid-order operation was accepted");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
