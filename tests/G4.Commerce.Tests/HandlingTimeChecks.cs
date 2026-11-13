using G4.Domain.Entities;
using G4.Infrastructure.Shipping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

// Seller handling time (scene 1): deadline starts when the order becomes Paid, the outbound
// label records the handover and the estimated arrival is anchored to the deadline.
internal static class HandlingTimeChecks
{
    private static void Check(bool condition, string message) =>
        ReturnAutomationChecks.Check(condition, message);

    public static async Task RunAsync()
    {
        ConfigChecks();
        await CheckoutStartsClockAsync();
        await TransitionRulesAsync();
        await HandoverAsync();
        Console.WriteLine(
            "Handling time: config, clock on Paid, legacy orders, on-time/late handover, ETA passed"
        );
    }

    private static IConfiguration Config(string handling = "120", string transit = "300") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Shipping:HandlingSeconds"] = handling,
                    ["Shipping:TransitSeconds"] = transit,
                }
            )
            .Build();

    private static ApplicationDbContext Db() =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    private static void ConfigChecks()
    {
        Check(
            ShippingHandlingPolicy.HandlingSeconds(null) == 172800
                && ShippingHandlingPolicy.TransitSeconds(null) == 259200,
            "missing config uses the two-day handling and three-day transit defaults"
        );
        Check(
            ShippingHandlingPolicy.HandlingSeconds(Config("0")) == 1
                && ShippingHandlingPolicy.HandlingSeconds(Config("999999999")) == 31536000,
            "handling seconds are clamped to a sane range"
        );
        Check(
            ShippingHandlingPolicy.HandlingSeconds(Config()) == 120
                && ShippingHandlingPolicy.TransitSeconds(Config()) == 300,
            "Development style config is read"
        );
    }

    private static async Task CheckoutStartsClockAsync()
    {
        await using var db = Db();
        db.Users.AddRange(
            new User
            {
                Id = 1,
                Role = "buyer",
                Email = "buyer@example.test",
            },
            new User
            {
                Id = 2,
                Role = "seller",
                Email = "seller@example.test",
            }
        );
        db.Addresses.AddRange(
            new Address
            {
                Id = 1,
                UserId = 1,
                FullName = "Buyer",
                Street = "Buyer street",
                City = "Hanoi",
                State = "Hanoi",
                Country = "Vietnam",
                Phone = "+84901112223",
            },
            new Address
            {
                Id = 2,
                UserId = 2,
                FullName = "Seller",
                Street = "Pickup street",
                City = "Hanoi",
                State = "Hanoi",
                Country = "Vietnam",
                IsDefault = true,
            }
        );
        db.Products.Add(
            new Product
            {
                Id = 1,
                SellerId = 2,
                Title = "Camera",
                Price = 100,
                WeightKg = 1.2m,
            }
        );
        db.Inventories.Add(
            new Inventory
            {
                Id = 1,
                ProductId = 1,
                Quantity = 10,
            }
        );
        await db.SaveChangesAsync();
        var config = Config();
        var checkout = new CheckoutService(
            db,
            new SellerFinanceService(db, config),
            config: config
        );
        var order = await checkout.CreateOrderAsync(
            1,
            new(1, [new(1, 1)], null, Guid.NewGuid().ToString())
        );
        Check(
            order.HandlingSeconds == 120 && order.HandlingDeadlineAt == null,
            "checkout snapshots handling seconds but the clock waits for payment"
        );
        var before = DateTime.UtcNow;
        await checkout.PayCardAsync(
            order.Id,
            "4111111111111111",
            "12/30",
            Guid.NewGuid().ToString()
        );
        var after = DateTime.UtcNow;
        var paid = await db.OrderTables.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Check(
            paid.Status == "Paid"
                && paid.HandlingDeadlineAt >= before.AddSeconds(120)
                && paid.HandlingDeadlineAt <= after.AddSeconds(120),
            "card payment starts the handling clock from the payment time"
        );

        var shipments = new ShipmentService(db, new ReturnAutomationChecks.Carrier(), config);
        var labelBefore = DateTime.UtcNow;
        var shipment = await shipments.CreateOutboundAsync(order.Id);
        var labelAfter = DateTime.UtcNow;
        var shipped = await db.OrderTables.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Check(
            shipped.HandedOverAt >= labelBefore
                && shipped.HandedOverAt <= labelAfter
                && ShippingHandlingPolicy.HandedOverOnTime(shipped) == true,
            "creating the label within the deadline is recorded as an on-time handover"
        );
        Check(
            shipment.EstimatedArrival == paid.HandlingDeadlineAt!.Value.AddSeconds(300),
            "estimated arrival is the handling deadline plus transit"
        );
    }

    private static async Task TransitionRulesAsync()
    {
        await using var db = Db();
        var fixedDeadline = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.OrderTables.AddRange(
            new OrderTable
            {
                Id = 1,
                Status = "AwaitingPayment",
                HandlingSeconds = 60,
            },
            new OrderTable { Id = 2, Status = "AwaitingPayment" },
            new OrderTable
            {
                Id = 3,
                Status = "CancelRequested",
                HandlingSeconds = 60,
                HandlingDeadlineAt = fixedDeadline,
            },
            new OrderTable
            {
                Id = 4,
                Status = "Paid",
                HandlingSeconds = 60,
            }
        );
        await db.SaveChangesAsync();
        var immediate = await db.OrderTables.SingleAsync(o => o.Id == 4);
        Check(
            immediate.HandlingDeadlineAt != null,
            "an order created already Paid (Buy It Now, zero total) starts the clock"
        );
        Check(
            (await db.OrderTables.SingleAsync(o => o.Id == 1)).HandlingDeadlineAt == null,
            "awaiting payment has no handling deadline"
        );

        var legacyPaid = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        immediate.HandlingDeadlineAt = legacyPaid;
        await db.SaveChangesAsync();
        immediate.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Check(
            immediate.HandlingDeadlineAt == legacyPaid,
            "saving an order that stays Paid never moves its deadline"
        );

        foreach (var order in await db.OrderTables.Where(o => o.Id <= 3).ToListAsync())
            order.Status = "Paid";
        await db.SaveChangesAsync();
        var rows = await db.OrderTables.AsNoTracking().ToDictionaryAsync(o => o.Id);
        Check(rows[1].HandlingDeadlineAt != null, "AwaitingPayment -> Paid starts the clock");
        Check(
            rows[2].HandlingDeadlineAt == null,
            "orders without a handling snapshot (pre-upgrade) never get a deadline"
        );
        Check(
            rows[3].HandlingDeadlineAt == fixedDeadline,
            "returning to Paid after a rejected cancel keeps the original deadline"
        );
    }

    private static async Task HandoverAsync()
    {
        await using var db = Db();
        var past = DateTime.UtcNow.AddMinutes(-5);
        db.OrderTables.AddRange(
            new OrderTable
            {
                Id = 1,
                Status = "Preparing",
                HandlingSeconds = 60,
                HandlingDeadlineAt = past,
            },
            new OrderTable { Id = 2, Status = "Paid" }
        );
        await db.SaveChangesAsync();
        var service = new ShipmentService(db, new ReturnAutomationChecks.Carrier(), Config());

        var late = await service.CreateOutboundAsync(1);
        var lateOrder = await db.OrderTables.AsNoTracking().SingleAsync(o => o.Id == 1);
        Check(
            ShippingHandlingPolicy.HandedOverOnTime(lateOrder) == false,
            "creating the label after the deadline is recorded as late, without blocking it"
        );
        Check(
            late.EstimatedArrival == past.AddSeconds(300),
            "shipping late does not push the promised arrival back"
        );
        var handedOver = lateOrder.HandedOverAt;
        await service.CreateOutboundAsync(1);
        Check(
            (await db.OrderTables.AsNoTracking().SingleAsync(o => o.Id == 1)).HandedOverAt
                == handedOver,
            "repeating label creation keeps the first handover time"
        );

        var before = DateTime.UtcNow;
        var legacy = await service.CreateOutboundAsync(2);
        var legacyOrder = await db.OrderTables.AsNoTracking().SingleAsync(o => o.Id == 2);
        Check(
            legacyOrder.HandedOverAt != null
                && ShippingHandlingPolicy.HandedOverOnTime(legacyOrder) == null,
            "legacy orders record the handover but are neither on time nor late"
        );
        Check(
            legacy.EstimatedArrival >= before.AddSeconds(300)
                && legacy.EstimatedArrival <= DateTime.UtcNow.AddSeconds(300),
            "legacy orders estimate arrival from the label time"
        );
    }
}
