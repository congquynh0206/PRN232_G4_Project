using System.Text.RegularExpressions;
using G4.Contracts.Checkout;
using G4.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

internal static class ReturnPolicySqlChecks
{
    public static async Task RunAsync()
    {
        var connection =
            Environment.GetEnvironmentVariable("G4_RETURN_SQL_TEST_CONNECTION")
            ?? throw new Exception("QA connection required");
        if (
            new SqlConnectionStringBuilder(connection).InitialCatalog
            != "G4_ReturnPolicy_Test_20261014"
        )
            throw new Exception("Dedicated return SQL QA database required");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection)
            .Options;
        await BootstrapAsync(connection, options);
        var clock = new Clock();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Finance:Levels:1:HoldSeconds"] = "30",
                    ["Returns:ReceiptSeconds"] = "45",
                }
            )
            .Build();
        var gateway = new Gateway();
        var carrier = new Carrier();
        int buyerId,
            sellerId,
            accountId,
            productId;
        var sellerEmail = $"return-seller-{Guid.NewGuid():N}@example.test";
        await using (var db = new ApplicationDbContext(options))
        {
            buyerId = (await db.Users.SingleAsync(x => x.Email == "buyer@example.test")).Id;
            var demoSeller = await db.Users.SingleAsync(x => x.Email == "seller@example.test");
            var seller = new User
            {
                Role = "seller",
                Username = "Return QA seller",
                Email = sellerEmail,
                Password = demoSeller.Password,
            };
            db.Users.Add(seller);
            await db.SaveChangesAsync();
            sellerId = seller.Id;
            var account = new SellerAccount
            {
                SellerId = sellerId,
                Level = 3,
                MonthlySalesLimit = 1000000m,
                SalesMonth = new DateTime(clock.Utc.Year, clock.Utc.Month, 1),
            };
            db.SellerAccounts.Add(account);
            await db.SaveChangesAsync();
            accountId = account.Id;
            productId = (await db.Products.FirstAsync()).Id;
        }
        async Task<int> AddOrder()
        {
            await using var db = new ApplicationDbContext(options);
            var order = new OrderTable
            {
                BuyerId = buyerId,
                SellerId = sellerId,
                Status = "Delivered",
                TotalPrice = 95m,
                ShippingFee = 5m,
                Subtotal = 100m,
                PricingSchemaVersion = 1,
                PlatformSubsidy = 10m,
                SellerGrossSnapshot = 105m,
                TotalWeightKg = 1m,
                OrderDate = clock.Utc,
                UpdatedAt = clock.Utc,
                PickupStateSnapshot = "Hanoi",
                PickupCountrySnapshot = "Vietnam",
                DeliveryStateSnapshot = "HCM",
                DeliveryCountrySnapshot = "Vietnam",
                PickupAddressSnapshot = "Seller, Road, City, Hanoi, Vietnam",
                AddressSnapshot = "Buyer, Road, City, HCM, Vietnam",
            };
            order.OrderItems.Add(
                new OrderItem
                {
                    ProductId = productId,
                    ProductTitleSnapshot = "Sản phẩm QA trả hàng",
                    UnitPrice = 100m,
                    Quantity = 1,
                    UnitWeightKgSnapshot = 1m,
                    SellerIdSnapshot = sellerId,
                    PlatformDiscountSnapshot = 10m,
                }
            );
            db.OrderTables.Add(order);
            await db.SaveChangesAsync();
            var payment = new Payment
            {
                OrderId = order.Id,
                UserId = buyerId,
                Amount = 95m,
                Status = "Succeeded",
                Method = "Card",
                PaidAt = clock.Utc,
            };
            db.Payments.Add(payment);
            db.ShippingInfos.Add(
                new ShippingInfo
                {
                    OrderId = order.Id,
                    Direction = "Outbound",
                    Status = "Delivered",
                    DeliveredAt = clock.Utc,
                    TrackingNumber = "QA-OUT-" + order.Id,
                    PickupAddressSnapshot = order.PickupAddressSnapshot,
                    DeliveryAddressSnapshot = order.AddressSnapshot,
                }
            );
            await db.SaveChangesAsync();
            await new SellerFinanceService(db, config, clock).RecordSuccessfulPaymentAsync(
                order.Id,
                payment.Id
            );
            return order.Id;
        }
        foreach (var reason in new[] { "WrongItem", "ChangedMind" })
        {
            var orderId = await AddOrder();
            decimal previousAvailable,
                previousDebt;
            ReturnQuote quote;
            await using (var db = new ApplicationDbContext(options))
            {
                var account = await db.SellerAccounts.SingleAsync(x => x.Id == accountId);
                previousAvailable = account.AvailableBalance;
                previousDebt = account.NegativeBalance;
                quote = await Service(db).QuoteAsync(orderId, reason);
            }
            var input = new ReturnRequestInput(
                reason,
                "SQL concurrent return",
                Guid.NewGuid().ToString(),
                quote.PricingFingerprint
            );
            async Task<int> Request()
            {
                await using var db = new ApplicationDbContext(options);
                return (await Service(db).RequestPricedReturnAsync(orderId, input)).Id;
            }
            var requested = await Task.WhenAll(Request(), Request());
            Check(requested[0] == requested[1], "SQL duplicate buyer requests create one return");
            var returnId = requested[0];
            async Task Approve()
            {
                await using var db = new ApplicationDbContext(options);
                await Service(db).ApproveAsync(returnId);
            }
            await Task.WhenAll(Approve(), Approve());
            await using (var db = new ApplicationDbContext(options))
            {
                Check(
                    await db.ShippingInfos.CountAsync(x =>
                        x.OrderId == orderId && x.Direction == "Return"
                    ) == 1,
                    "SQL one return label"
                );
                Check(
                    await db.FinancialTransactions.CountAsync(x =>
                        x.OrderId == orderId && x.Type == "ReturnShippingCost"
                    ) == 1,
                    "SQL one postage charge"
                );
                var parcel = await db.ShippingInfos.SingleAsync(x =>
                    x.OrderId == orderId && x.Direction == "Return"
                );
                parcel.Status = "Delivered";
                parcel.DeliveredAt = clock.Utc.AddSeconds(-50);
                await db.SaveChangesAsync();
            }
            var callsBefore = gateway.Calls;
            async Task Receive()
            {
                await using var db = new ApplicationDbContext(options);
                await Service(db).MarkReceivedAsync(returnId);
            }
            async Task Worker()
            {
                await using var db = new ApplicationDbContext(options);
                await Service(db).MaintainAsync();
            }
            await Task.WhenAll(Receive(), Receive(), Worker());
            Check(
                gateway.Calls - callsBefore == 1,
                "SQL manual and worker receipt refund exactly once"
            );
            await using (var db = new ApplicationDbContext(options))
            {
                var refund = await db.Refunds.SingleAsync(x => x.ReturnRequestId == returnId);
                Check(
                    refund.Status == "Succeeded" && refund.Amount == quote.BuyerRefund,
                    "SQL refund equals accepted preview"
                );
                var account = await db.SellerAccounts.SingleAsync(x => x.Id == accountId);
                Check(account.OnHoldBalance == 0, "SQL return leaves no stale held balance");
                var expectedNet =
                    previousAvailable
                    - previousDebt
                    - 92.10m
                    + (reason == "ChangedMind" ? 4.39m : -5m);
                Check(
                    account.AvailableBalance == Math.Max(0m, expectedNet)
                        && account.NegativeBalance == Math.Max(0m, -expectedNet),
                    "SQL wallet and debt reconcile across returned sales"
                );
            }
        }
        var previewOrderId = await AddOrder();
        Directory.CreateDirectory("artifacts/return-policy");
        await File.WriteAllTextAsync(
            "artifacts/return-policy/qa-fixture.json",
            System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    previewOrderId,
                    buyerEmail = "buyer@example.test",
                    sellerEmail,
                }
            )
        );
        Console.WriteLine(
            "SQL return pricing migration, duplicate request/label/postage, simultaneous manual/worker refund and Instant wallet reconciliation passed"
        );

        ReturnService Service(ApplicationDbContext db) =>
            new(db, gateway, carrier, new SellerFinanceService(db, config, clock), config, clock);
    }

    private static async Task BootstrapAsync(
        string connection,
        DbContextOptions<ApplicationDbContext> options
    )
    {
        await using var db = new ApplicationDbContext(options);
        var creator = db.Database.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync())
            await creator.CreateAsync();
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync();
        await using var exists = new SqlCommand("SELECT OBJECT_ID(N'[User]')", sql);
        if (await exists.ExecuteScalarAsync() is DBNull or null)
        {
            var baseline = Regex.Replace(
                await File.ReadAllTextAsync("database/baseline.sql"),
                @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE CloneEbayDB;)\s*$",
                ""
            );
            await ExecuteAsync(baseline);
        }
        await db.Database.MigrateAsync();
        await ExecuteAsync(
            (await File.ReadAllTextAsync("database/seed-data.sql")).Replace(
                "USE [CloneEbayDB];",
                "USE [G4_ReturnPolicy_Test_20261014];"
            )
        );
        async Task ExecuteAsync(string script)
        {
            foreach (var batch in Regex.Split(script, @"(?im)^\s*GO\s*$"))
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    await using var command = new SqlCommand(batch, sql);
                    await command.ExecuteNonQueryAsync();
                }
        }
    }

    private static void Check(bool value, string name)
    {
        if (!value)
            throw new Exception(name);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public DateTime Utc => now.UtcDateTime;

        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Carrier : ICarrierGateway
    {
        public Task<string> CreateLabelAsync(
            int id,
            string direction,
            string key,
            CancellationToken ct
        ) => Task.FromResult(key);
    }

    private sealed class Gateway : IRefundGateway
    {
        public int Calls;

        public Task<string> RefundAsync(
            Payment payment,
            decimal amount,
            string key,
            CancellationToken ct
        )
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult("SQL-" + key);
        }
    }
}
