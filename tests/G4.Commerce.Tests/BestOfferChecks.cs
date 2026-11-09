using G4.Contracts.Checkout;
using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

// Best Offer: 5 offers per buyer per product across sessions, counteroffers between the two
// prices, pay-later (unpaid order) or Auto-Pay on accept, and idempotent retries.
internal static class BestOfferChecks
{
    private const int Buyer = 1;
    private const int Seller = 2;
    private const int ProductId = 1;

    public static async Task RunAsync()
    {
        await QuotaAcrossSessionsAsync();
        await CounterRangeAsync();
        await PayLaterAsync();
        await AutoPayAsync();
        await IdempotentRetryAsync();
        Console.WriteLine(
            "Best offers: 5 offers per product across sessions, counteroffers between the two "
                + "prices, pay-later unpaid order with seller cancel and strike, Auto-Pay charge, "
                + "idempotent accept/decline/counter retries"
        );
    }

    private static async Task QuotaAcrossSessionsAsync()
    {
        await using var db = await FixtureAsync();
        var offers = Service(db);
        var first = await offers.CreateAsync(Buyer, Create(50m, "q-1"), default);
        await offers.SellerDeclineAsync(Seller, first.Id, Key("q-1-decline"), default);

        var second = await offers.CreateAsync(Buyer, Create(51m, "q-2"), default);
        await offers.SellerCounterAsync(Seller, second.Id, Counter(90m, "q-2-s"), default);
        var countered = await offers.BuyerCounterAsync(
            Buyer,
            second.Id,
            Counter(60m, "q-2-b"),
            default
        );
        Check(countered.BuyerOfferCount == 3, "a buyer counteroffer uses one product offer");
        await offers.SellerDeclineAsync(Seller, second.Id, Key("q-2-decline"), default);

        for (var i = 3; i <= 4; i++)
        {
            var next = await offers.CreateAsync(Buyer, Create(50m + i, $"q-{i}"), default);
            await offers.SellerDeclineAsync(Seller, next.Id, Key($"q-{i}-decline"), default);
        }
        var catalog = await offers.GetOfferableProductsAsync(Buyer, default);
        Check(
            catalog.Single().BuyerOffersUsed == 5 && catalog.Single().BuyerOfferLimit == 5,
            "the catalog shows 5/5 offers used for the product"
        );
        await RejectAsync(db, () => offers.CreateAsync(Buyer, Create(55m, "q-6"), default));
        Check(
            await db.BestOffers.CountAsync() == 4,
            "a new session cannot reset the 5-offer limit"
        );
    }

    private static async Task CounterRangeAsync()
    {
        await using var db = await FixtureAsync();
        var offers = Service(db);
        var offer = await offers.CreateAsync(Buyer, Create(70m, "r-1"), default);
        foreach (var amount in new[] { 69m, 70m, 100m, 120m })
        {
            var input = Counter(amount, $"r-s-{amount}");
            await RejectAsync(db, () => offers.SellerCounterAsync(Seller, offer.Id, input, default));
        }
        var seller = await offers.SellerCounterAsync(
            Seller,
            offer.Id,
            Counter(85m, "r-s"),
            default
        );
        Check(seller.CurrentAmount == 85m, "the seller counters between the offer and list price");
        foreach (var amount in new[] { 65m, 70m, 85m, 90m })
        {
            var input = Counter(amount, $"r-b-{amount}");
            await RejectAsync(db, () => offers.BuyerCounterAsync(Buyer, offer.Id, input, default));
        }
        var buyer = await offers.BuyerCounterAsync(Buyer, offer.Id, Counter(75m, "r-b"), default);
        Check(
            buyer.CurrentAmount == 75m && buyer.Status == "PendingSeller",
            "the buyer counters between their last offer and the seller counter"
        );
    }

    private static async Task PayLaterAsync()
    {
        await using var db = await FixtureAsync();
        var offers = Service(db, ("Checkout:PaymentHoldMinutes", "5"));
        var before = DateTime.UtcNow;
        var offer = await offers.CreateAsync(Buyer, Create(80m, "p-1"), default);
        var accepted = await offers.SellerAcceptAsync(Seller, offer.Id, Key("p-1-accept"), default);
        Check(
            accepted.Status == "Accepted" && accepted.OrderId != null,
            "accepting a pay-later offer creates an order"
        );
        var order = await db.OrderTables.Include(x => x.OrderItems)
            .SingleAsync(x => x.Id == accepted.OrderId);
        Check(order.Status == "AwaitingPayment", "the pay-later order waits for payment");
        var minutes = (order.PaymentExpiresAt!.Value - before).TotalMinutes;
        Check(minutes is > 4.9 and < 5.2, $"the hold follows PaymentHoldMinutes, got {minutes}");
        Check(order.OrderItems.Single().UnitPrice == 80m, "the order uses the agreed price");
        Check(order.DeliveryPostalCodeSnapshot == "10024", "the address snapshot is complete");
        Check(!await db.Payments.AnyAsync(), "no money is collected before the buyer pays");
        Check((await db.Inventories.FindAsync(1))!.Quantity == 4, "the item is held at once");

        // After the deadline the existing unpaid flow applies: seller cancel and one strike.
        order.PaymentExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        var checkout = new CheckoutService(
            db,
            config: Config(("Checkout:AutoExpireUnpaid", "false"))
        );
        var cancelled = await checkout.SellerCancelUnpaidAsync(order.Id, Seller, null);
        Check(cancelled.Status == "Cancelled", "the seller cancels the overdue offer order");
        Check(
            await db.UnpaidItemStrikes.CountAsync(x => x.BuyerId == Buyer) == 1,
            "the buyer receives one Unpaid Item Strike"
        );
        Check((await db.Inventories.FindAsync(1))!.Quantity == 5, "the stock is restored");

        var banned = await db.Users.SingleAsync(x => x.Id == Buyer);
        banned.BannedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var late = await offers.CreateAsync(Buyer, Create(81m, "p-2"), default);
        await RejectAsync(
            db,
            () => offers.SellerAcceptAsync(Seller, late.Id, Key("p-2-accept"), default)
        );
        Check(
            await db.OrderTables.CountAsync() == 1,
            "a banned buyer cannot get a new unpaid order"
        );
    }

    private static async Task AutoPayAsync()
    {
        await using var db = await FixtureAsync();
        var offers = Service(db);
        var declined = await offers.CreateAsync(
            Buyer,
            Create(80m, "a-1", "Card", "Declined"),
            default
        );
        await RejectAsync(
            db,
            () => offers.SellerAcceptAsync(Seller, declined.Id, Key("a-1-accept"), default)
        );
        Check(!await db.OrderTables.AnyAsync(), "a declined card creates no order");

        await offers.UpdatePaymentProfileAsync(
            Buyer,
            declined.Id,
            new BestOfferActionInput(CardProfile: "Success", CardExpiry: "12/30"),
            default
        );
        var paid = await offers.SellerAcceptAsync(Seller, declined.Id, Key("a-1-retry"), default);
        var order = await db.OrderTables.SingleAsync(x => x.Id == paid.OrderId);
        Check(order.Status == "Paid", "Auto-Pay turns the accepted offer into a paid order");
        Check(
            await db.Payments.CountAsync(x => x.OrderId == order.Id && x.Status == "Succeeded")
                == 1,
            "Auto-Pay records one successful payment"
        );
        Check((await db.Inventories.FindAsync(1))!.Quantity == 4, "Auto-Pay takes the stock");

        var later = await offers.CreateAsync(Buyer, Create(60m, "a-2"), default);
        await RejectAsync(
            db,
            () => offers.UpdatePaymentProfileAsync(
                Buyer,
                later.Id,
                new BestOfferActionInput(CardProfile: "Success", CardExpiry: "12/30"),
                default
            )
        );
    }

    private static async Task IdempotentRetryAsync()
    {
        await using var db = await FixtureAsync();
        var offers = Service(db);
        var offer = await offers.CreateAsync(Buyer, Create(70m, "i-1"), default);
        await offers.SellerCounterAsync(Seller, offer.Id, Counter(90m, "i-counter"), default);
        var replayed = await offers.SellerCounterAsync(
            Seller,
            offer.Id,
            Counter(95m, "i-counter"),
            default
        );
        Check(
            replayed.CurrentAmount == 90m && replayed.Turns.Count == 2,
            "a counter retry returns the stored result without a new turn"
        );

        var first = await offers.BuyerAcceptAsync(Buyer, offer.Id, Key("i-accept"), default);
        var again = await offers.BuyerAcceptAsync(Buyer, offer.Id, Key("i-accept"), default);
        Check(
            again.Status == "Accepted" && again.OrderId == first.OrderId,
            "an accept retry returns the same order instead of an error"
        );
        Check(await db.OrderTables.CountAsync() == 1, "an accept retry creates no second order");
        await RejectAsync(
            db,
            () => offers.BuyerAcceptAsync(Buyer, offer.Id, Key("i-new-key"), default)
        );

        var other = await offers.CreateAsync(Buyer, Create(60m, "i-2"), default);
        await RejectAsync(
            db,
            () => offers.SellerDeclineAsync(Seller, other.Id, Key("i-accept"), default)
        );
        var closed = await offers.SellerDeclineAsync(Seller, other.Id, Key("i-decline"), default);
        var closedAgain = await offers.SellerDeclineAsync(
            Seller,
            other.Id,
            Key("i-decline"),
            default
        );
        Check(
            closed.Status == "Declined" && closedAgain.Status == "Declined",
            "a decline retry returns the declined session"
        );
    }

    private static BestOfferService Service(
        ApplicationDbContext db,
        params (string Key, string Value)[] values
    )
    {
        var config = Config(values);
        return new BestOfferService(
            db,
            new SellerFinanceService(db, config),
            TimeProvider.System,
            config
        );
    }

    private static BestOfferCreateInput Create(
        decimal amount,
        string key,
        string method = "PayLater",
        string card = "Success"
    ) =>
        new(
            ProductId,
            amount,
            1,
            method,
            method == "Card" ? card : null,
            method == "Card" ? "12/30" : null,
            key
        );

    private static BestOfferActionInput Key(string key) => new(Key: key);

    private static BestOfferActionInput Counter(decimal amount, string key) =>
        new(Amount: amount, Key: key);

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                values.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value))
            )
            .Build();

    private static async Task<ApplicationDbContext> FixtureAsync()
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        db.Users.Add(new User { Id = Buyer, Role = "buyer", Email = "buyer@example.test" });
        db.Users.Add(new User { Id = Seller, Role = "seller" });
        db.Addresses.Add(
            new Address
            {
                Id = 1,
                UserId = Buyer,
                FullName = "Test Person-us",
                Street = "Test Street 13",
                City = "New York",
                State = "NY",
                Country = "US",
                PostalCode = "10024",
            }
        );
        db.Addresses.Add(
            new Address
            {
                Id = 2,
                UserId = Seller,
                IsDefault = true,
                State = "Hanoi",
                Country = "Vietnam",
            }
        );
        db.Products.Add(
            new Product
            {
                Id = ProductId,
                SellerId = Seller,
                Title = "Offer item",
                Price = 100m,
                WeightKg = 1m,
                AllowOffers = true,
            }
        );
        db.Inventories.Add(new Inventory { Id = 1, ProductId = ProductId, Quantity = 5 });
        await db.SaveChangesAsync();
        return db;
    }

    // InMemory has no transaction rollback, so drop whatever a rejected call left tracked.
    private static async Task RejectAsync(ApplicationDbContext db, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            db.ChangeTracker.Clear();
            return;
        }
        throw new Exception("An invalid Best Offer operation was accepted");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
