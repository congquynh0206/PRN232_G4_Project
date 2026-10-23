using System.Text.RegularExpressions;
using G4.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

internal static class ReturnPayoutSqlChecks
{
    private const string Qa = "G4_ReturnWindowPayout_Test_20261020";
    private static readonly Microsoft.Extensions.Configuration.IConfiguration Config =
        ReturnWindowChecks.Config();

    private static SellerFinanceService Finance(
        ApplicationDbContext db,
        TimeProvider? clock = null
    ) => new(db, Config, clock);

    public static async Task RunAsync()
    {
        var connection =
            Environment.GetEnvironmentVariable("G4_RETURN_PAYOUT_SQL_CONNECTION")
            ?? throw new Exception("RT1/PAY1 dedicated QA connection required");
        if (new SqlConnectionStringBuilder(connection).InitialCatalog != Qa)
            throw new Exception("Only " + Qa + " is allowed");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection)
            .Options;
        await Prepare(options, connection);
        await ReturnCloseRace(options);
        await DeliveryRollback(options);
        foreach (var level in new[] { 1, 2, 3 })
            await PayoutFlow(options, level);
        await PayoutFundsRace(options);
        await PayoutRollback(options, "Created");
        await PayoutRollback(options, "Completed");
        await PayoutRollback(options, "Returned");
        Console.WriteLine(
            "RT1/PAY1 real SQL passed: legacy deadlines/payout preservation, request/close race, delivery snapshot rollback, all-level request/worker concurrency, funds reservation, immutable history, summary, terminal-only outbox and finance/history/realtime rollback/retry"
        );
    }

    private static async Task Prepare(
        DbContextOptions<ApplicationDbContext> options,
        string connection
    )
    {
        await using var db = new ApplicationDbContext(options);
        var creator = db.Database.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync())
            await creator.CreateAsync();
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync();
        await using var count = new SqlCommand(
            "SELECT COUNT(*) FROM sys.tables WHERE name <> '__EFMigrationsHistory'",
            sql
        );
        if (Convert.ToInt32(await count.ExecuteScalarAsync()) == 0)
        {
            var baseline = Regex.Replace(
                await File.ReadAllTextAsync("database/baseline.sql"),
                @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE CloneEbayDB;)\s*$",
                ""
            );
            foreach (var batch in Regex.Split(baseline, @"(?im)^\s*GO\s*$"))
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    await using var cmd = new SqlCommand(batch, sql);
                    await cmd.ExecuteNonQueryAsync();
                }
            await db
                .Database.GetService<IMigrator>()
                .MigrateAsync("20261020153620_AddAddressChangeConfirmations");
            await using var legacy = new SqlCommand(
                @"
                INSERT INTO OrderTable ([status]) VALUES (N'Delivered'); DECLARE @order int = SCOPE_IDENTITY();
                INSERT INTO ShippingInfo (orderId, [status], Direction, TrackingNumber, CreatedAt, DeliveredAt)
                VALUES (@order, N'Delivered', N'Outbound', N'RT1-LEGACY', '2026-10-18', '2026-10-18');
                INSERT INTO ReturnRequest (orderId, [status], CreatedAt, ReturnDeadline)
                VALUES (@order, N'Rejected', '2026-10-19', '2026-10-26');
                INSERT INTO [User] (email, [role]) VALUES (N'rt1-pay1-legacy@example.test', N'seller'); DECLARE @seller int = SCOPE_IDENTITY();
                INSERT INTO SellerAccount (SellerId, Level, Status, MonthlySalesLimit, HoldDays, ProcessingBalance, AvailableBalance, OnHoldBalance, NegativeBalance, MonthlySalesAmount, SalesMonth, UpdatedAt)
                VALUES (@seller, 3, N'Active', 1000000, 0, 0, 87.70, 0, 0, 0, '2026-10-01', '2026-10-19'); DECLARE @account int = SCOPE_IDENTITY();
                INSERT INTO SellerPayout (SellerAccountId, Amount, Currency, Status, IdempotencyKey, DestinationMasked, BankReferenceId, SimulateFailure, CreatedAt, UpdatedAt, CompletedAt)
                VALUES (@account, 20, N'USD', N'Completed', N'pay1-legacy', N'****1234', N'LEGACY-BANK', 0, '2026-10-19', '2026-10-19', '2026-10-19');",
                sql
            );
            await legacy.ExecuteNonQueryAsync();
        }
        await db.Database.MigrateAsync();
        var oldDelivery = await db
            .ShippingInfos.AsNoTracking()
            .SingleAsync(s => s.TrackingNumber == "RT1-LEGACY");
        var oldOrder = await db
            .OrderTables.AsNoTracking()
            .SingleAsync(o => o.Id == oldDelivery.OrderId);
        Check(
            oldOrder.ReturnWindowPolicyVersion == 0
                && oldOrder.ReturnWindowSeconds == 604800
                && oldOrder.ReturnWindowStartedAt == new DateTime(2026, 10, 18)
                && oldOrder.ReturnWindowClosesAt == new DateTime(2026, 10, 25),
            "migration preserves legacy seven-day entitlement instead of shrinking to current demo"
        );
        Check(
            (
                await db.ReturnRequests.AsNoTracking().SingleAsync(r => r.OrderId == oldOrder.Id)
            ).ReturnDeadline == new DateTime(2026, 10, 26),
            "historical return request deadline is not rewritten"
        );
        var oldPayout = await db
            .SellerPayouts.AsNoTracking()
            .SingleAsync(p => p.IdempotencyKey == "pay1-legacy");
        Check(
            oldPayout.WorkflowVersion == 0
                && oldPayout.Status == "Completed"
                && oldPayout.BankReferenceId == "LEGACY-BANK"
                && oldPayout.CompletedAt == new DateTime(2026, 10, 19)
                && !await db.SellerPayoutEvents.AnyAsync(e => e.PayoutId == oldPayout.Id),
            "legacy terminal payout has no invented workflow/history"
        );
        Check(
            (
                await db
                    .SellerAccounts.AsNoTracking()
                    .SingleAsync(a => a.Id == oldPayout.SellerAccountId)
            ).AvailableBalance == 87.70m,
            "migration does not rewrite wallet"
        );
    }

    private sealed record OrderFixture(int Order, int Buyer, int Seller, int Shipper, int Shipment);

    private static async Task<OrderFixture> SeedOrder(
        DbContextOptions<ApplicationDbContext> options
    )
    {
        await using var db = new ApplicationDbContext(options);
        var token = Guid.NewGuid().ToString("N");
        var buyer = new User { Role = "buyer", Email = token + "-buyer@example.test" };
        var seller = new User { Role = "seller", Email = token + "-seller@example.test" };
        var shipper = new User { Role = "shipper", Email = token + "-shipper@example.test" };
        db.Users.AddRange(buyer, seller, shipper);
        await db.SaveChangesAsync();
        var address = new Address
        {
            UserId = buyer.Id,
            FullName = "Buyer",
            Street = "Delivery",
            City = "Hanoi",
            State = "Hanoi",
            Country = "Vietnam",
        };
        var pickup = new Address
        {
            UserId = seller.Id,
            FullName = "Seller",
            Street = "Pickup",
            City = "Hanoi",
            State = "Hanoi",
            Country = "Vietnam",
            IsDefault = true,
        };
        var product = new Product
        {
            SellerId = seller.Id,
            Title = "RT1",
            Price = 100,
            WeightKg = 1.2m,
        };
        db.Addresses.AddRange(address, pickup);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.Inventories.Add(new() { ProductId = product.Id, Quantity = 10 });
        await db.SaveChangesAsync();
        var checkout = new CheckoutService(db, Finance(db), Config);
        var order = await checkout.CreateOrderAsync(
            buyer.Id,
            new(address.Id, [new(product.Id, 1)], null, token)
        );
        await checkout.PayCardAsync(order.Id, "4111111111111111", "12/30", token);
        var shipping = new ShipmentService(db, new ReturnAutomationChecks.Carrier(), Config);
        var s = await shipping.CreateOutboundAsync(order.Id);
        await shipping.ClaimAsync(s.Id, shipper.Id);
        foreach (var status in new[] { "PickedUp", "InTransit", "OutForDelivery" })
            await shipping.RecordEventAsync(s.Id, token + status, status, shipperId: shipper.Id);
        return new(order.Id, buyer.Id, seller.Id, shipper.Id, s.Id);
    }

    private static async Task ReturnCloseRace(DbContextOptions<ApplicationDbContext> options)
    {
        var f = await SeedOrder(options);
        var clock = new AddressConfirmationChecks.Clock();
        DateTime deadline;
        G4.Contracts.Checkout.ReturnRequestInput input;
        await using (var db = new ApplicationDbContext(options))
        {
            await new ShipmentService(
                db,
                new ReturnAutomationChecks.Carrier(),
                Config,
                clock
            ).RecordEventAsync(
                f.Shipment,
                "rt1-deliver-" + f.Order,
                "Delivered",
                shipperId: f.Shipper
            );
            deadline = clock.Utc.AddSeconds(300);
            clock.Utc = clock.Utc.AddSeconds(299);
            var q = await new ReturnService(
                db,
                new ReturnAutomationChecks.Gateway(),
                new ReturnAutomationChecks.Carrier(),
                Finance(db, clock),
                Config,
                clock
            ).QuoteAsync(f.Order, "WrongItem");
            Check(q.ReturnWindowClosesAt == deadline, "SQL quote uses delivery snapshot");
            input = new(
                "WrongItem",
                "Timely SQL request",
                "sql-return-" + f.Order,
                q.PricingFingerprint
            );
        }
        var closeClock = new AddressConfirmationChecks.Clock { Utc = deadline };
        async Task Request()
        {
            await using var db = new ApplicationDbContext(options);
            try
            {
                await new ReturnService(
                    db,
                    new ReturnAutomationChecks.Gateway(),
                    new ReturnAutomationChecks.Carrier(),
                    Finance(db, clock),
                    Config,
                    clock
                ).RequestPricedReturnAsync(f.Order, input);
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains("hết hạn") || ex.Message.Contains("giao thành công")) { }
        }
        async Task Close()
        {
            await using var db = new ApplicationDbContext(options);
            await new CheckoutService(
                db,
                Finance(db, closeClock),
                Config,
                clock: closeClock
            ).CloseDeliveredOutsideReturnWindowAsync();
        }
        await Task.WhenAll(Request(), Close());
        await using var verify = new ApplicationDbContext(options);
        var order = await verify.OrderTables.SingleAsync(o => o.Id == f.Order);
        var request = await verify.ReturnRequests.SingleOrDefaultAsync(r => r.OrderId == f.Order);
        Check(
            request == null
                ? order.Status == "Closed"
                : order.Status == "Delivered" && request.ReturnDeadline == deadline,
            "SQL request/close race cannot close a timely accepted return"
        );
        Check(
            order.ReturnWindowClosesAt == deadline && order.ReturnWindowSeconds == 300,
            "SQL policy remains stable"
        );
    }

    private static async Task DeliveryRollback(DbContextOptions<ApplicationDbContext> options)
    {
        var f = await SeedOrder(options);
        var failed = new DbContextOptionsBuilder<ApplicationDbContext>(options)
            .AddInterceptors(new FailSave("Delivery"))
            .Options;
        await using (var db = new ApplicationDbContext(failed))
            await Reject(() =>
                new ShipmentService(
                    db,
                    new ReturnAutomationChecks.Carrier(),
                    Config
                ).RecordEventAsync(
                    f.Shipment,
                    "rt1-failed-delivery-" + f.Order,
                    "Delivered",
                    shipperId: f.Shipper
                )
            );
        await using var verify = new ApplicationDbContext(options);
        Check(
            (await verify.OrderTables.SingleAsync(o => o.Id == f.Order)).ReturnWindowClosesAt
                == null
                && (await verify.ShippingInfos.SingleAsync(s => s.Id == f.Shipment)).Status
                    == "OutForDelivery"
                && !await verify.ShippingEvents.AnyAsync(e =>
                    e.ExternalEventId == "rt1-failed-delivery-" + f.Order
                ),
            "delivery/window/event rollback together"
        );
        await new ShipmentService(
            verify,
            new ReturnAutomationChecks.Carrier(),
            Config
        ).RecordEventAsync(
            f.Shipment,
            "rt1-failed-delivery-" + f.Order,
            "Delivered",
            shipperId: f.Shipper
        );
    }

    private static async Task<(int Seller, int Account)> SeedAccount(
        DbContextOptions<ApplicationDbContext> options,
        int level
    )
    {
        await using var db = new ApplicationDbContext(options);
        var seller = new User
        {
            Role = "seller",
            Email = Guid.NewGuid().ToString("N") + "@example.test",
        };
        db.Users.Add(seller);
        await db.SaveChangesAsync();
        var account = new SellerAccount
        {
            SellerId = seller.Id,
            Level = level,
            AvailableBalance = 87.70m,
        };
        db.SellerAccounts.Add(account);
        await db.SaveChangesAsync();
        return (seller.Id, account.Id);
    }

    private static async Task Advance(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        await Finance(db).AdvancePayoutsAsync();
    }

    private static async Task PayoutFlow(DbContextOptions<ApplicationDbContext> options, int level)
    {
        var f = await SeedAccount(options, level);
        var key = "pay1-" + f.Seller;
        async Task<int> Request()
        {
            await using var db = new ApplicationDbContext(options);
            return (await Finance(db).RequestPayoutAsync(f.Seller, 50m, key, false)).Id;
        }
        var requests = await Task.WhenAll(Request(), Request());
        var id = requests[0];
        Check(requests.All(x => x == id), "same-key SQL payout requests reserve once");
        await using (var db = new ApplicationDbContext(options))
        {
            var p = await db.SellerPayouts.SingleAsync(x => x.Id == id);
            var summary = await Finance(db).GetSummaryAsync(f.Seller);
            Check(
                p.Status == "Created"
                    && p.CompletedAt == null
                    && summary.Available == 37.70m
                    && summary.PayoutPending == 50m
                    && summary.PaidOut == 0,
                "SQL all levels reserve pending, never instantly complete"
            );
            Check(
                !await db.NotificationOutbox.AnyAsync(x => x.PayoutId == id),
                "SQL pending has no completion mail"
            );
        }
        await Task.WhenAll(Advance(options), Advance(options));
        await using (var db = new ApplicationDbContext(options))
            Check(
                (await db.SellerPayouts.SingleAsync(x => x.Id == id)).Status == "FundsSent",
                "concurrent workers retain intermediate bank acknowledgement state"
            );
        await Task.WhenAll(Advance(options), Advance(options));
        await using var verify = new ApplicationDbContext(options);
        var final = await Finance(verify).GetSummaryAsync(f.Seller);
        Check(
            final.Available == 37.70m
                && final.PayoutPending == 0
                && final.PaidOut == 50m
                && final
                    .Payouts.Single()
                    .History!.Select(x => x.ToStatus)
                    .SequenceEqual(new[] { "Created", "InProgress", "FundsSent", "Completed" }),
            "SQL step history and totals are exact after concurrent workers"
        );
        Check(
            await verify.FinancialTransactions.CountAsync(x =>
                x.PayoutId == id && x.Type == "Payout"
            ) == 1
                && await verify.NotificationOutbox.CountAsync(x =>
                    x.PayoutId == id && x.EventType == "PayoutCompleted"
                ) == 1,
            "SQL one debit and final mail"
        );
    }

    private static async Task PayoutFundsRace(DbContextOptions<ApplicationDbContext> options)
    {
        var f = await SeedAccount(options, 3);
        async Task<bool> Request(string key)
        {
            await using var db = new ApplicationDbContext(options);
            try
            {
                await Finance(db).RequestPayoutAsync(f.Seller, 50m, key, false);
                return true;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("insufficient"))
            {
                return false;
            }
        }
        var results = await Task.WhenAll(
            Request("funds-a-" + f.Seller),
            Request("funds-b-" + f.Seller)
        );
        await using var verify = new ApplicationDbContext(options);
        Check(
            results.Count(x => x) == 1
                && (
                    await verify.SellerAccounts.SingleAsync(a => a.Id == f.Account)
                ).AvailableBalance == 37.70m,
            "different concurrent payout keys cannot spend pending funds again"
        );
        for (var i = 0; i < 3; i++)
            await Advance(options);
    }

    private static async Task PayoutRollback(
        DbContextOptions<ApplicationDbContext> options,
        string stage
    )
    {
        var f = await SeedAccount(options, 3);
        var key = "rollback-" + stage + f.Seller;
        int id = 0,
            signals;
        if (stage != "Created")
        {
            await using (var db = new ApplicationDbContext(options))
                id = (
                    await Finance(db).RequestPayoutAsync(f.Seller, 50m, key, stage == "Returned")
                ).Id;
            await Advance(options);
            await Advance(options);
        }
        await using (var db = new ApplicationDbContext(options))
            signals = await db.CommerceChanges.CountAsync();
        var failed = new DbContextOptionsBuilder<ApplicationDbContext>(options)
            .AddInterceptors(new FailSave(stage))
            .Options;
        await using (var db = new ApplicationDbContext(failed))
            if (stage == "Created")
                await Reject(() => Finance(db).RequestPayoutAsync(f.Seller, 50m, key, false));
            else
                await Reject(() => Finance(db).AdvancePayoutsAsync());
        await using (var verify = new ApplicationDbContext(options))
        {
            Check(
                (await verify.SellerAccounts.SingleAsync(a => a.Id == f.Account)).AvailableBalance
                    == (stage == "Created" ? 87.70m : 37.70m),
                "SQL payout failure rolls wallet back"
            );
            Check(
                await verify.CommerceChanges.CountAsync() == signals
                    && !await verify.NotificationOutbox.AnyAsync(x =>
                        x.PayoutId == id && x.PayoutId != null
                    ),
                "failed payout transaction publishes no terminal mail/realtime"
            );
            if (stage == "Created")
                Check(
                    !await verify.SellerPayouts.AnyAsync(x => x.IdempotencyKey == key),
                    "failed request creates no committed payout"
                );
            else
                Check(
                    (await verify.SellerPayouts.SingleAsync(x => x.Id == id)).Status == "FundsSent"
                        && !await verify.SellerPayoutEvents.AnyAsync(x =>
                            x.PayoutId == id && x.ToStatus == stage
                        ),
                    "failed terminal step leaves funds-sent state/history intact"
                );
            if (stage == "Created")
                id = (await Finance(verify).RequestPayoutAsync(f.Seller, 50m, key, false)).Id;
        }
        for (var i = 0; i < 3; i++)
            await Advance(options);
        await using var done = new ApplicationDbContext(options);
        Check(
            (await done.SellerPayouts.SingleAsync(x => x.Id == id)).Status
                == (stage == "Returned" ? "Returned" : "Completed"),
            "retry reaches one terminal result after rollback"
        );
        Check(
            (await done.SellerAccounts.SingleAsync(a => a.Id == f.Account)).AvailableBalance
                == (stage == "Returned" ? 87.70m : 37.70m)
                && await done.NotificationOutbox.CountAsync(x => x.PayoutId == id) == 1,
            "retry restores or completes funds once with one terminal email"
        );
    }

    private sealed class FailSave(string stage) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData d,
            int result,
            CancellationToken ct = default
        )
        {
            var c = d.Context!;
            var fail =
                stage == "Delivery"
                    ? c
                        .ChangeTracker.Entries<ShippingEvent>()
                        .Any(e => e.Entity.Status == "Delivered")
                    : c
                        .ChangeTracker.Entries<SellerPayoutEvent>()
                        .Any(e => e.Entity.ToStatus == stage);
            if (fail)
                throw new InvalidOperationException(
                    "Simulated failure after " + stage + " save, before commit"
                );
            return ValueTask.FromResult(result);
        }
    }

    private static void Check(bool value, string message) =>
        ReturnAutomationChecks.Check(value, message);

    private static Task Reject(Func<Task> work) =>
        ShipmentReattemptChecks.Reject<InvalidOperationException>(work);
}
