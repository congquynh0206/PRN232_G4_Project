using G4.Application.Integrations;
using G4.Domain.Entities;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Integrations.Refunds;
using G4.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class KlarnaChecks
{
    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Frontend:PublicBaseUrl"] = "http://localhost:5131"
        }).Build();

    public static async Task RunAsync()
    {
        await AuthorizeCaptureAndRefund();
        await RejectProviderMismatch();
        await FailedSessionStaysRetryable();
        await CancelledSessionReleasesAttempt();
        await MaintenanceReconcilesAbandonedSession();
        await MaintenanceDisablesExpiredSession();
        await VoidUncapturedAuthorization();
        await RejectMissingUsZip();
        await UseProviderHppExpiry();
        Console.WriteLine("Klarna HPP authorization, capture, finance, refund, idempotency and mismatch checks passed");
    }

    private static async Task AuthorizeCaptureAndRefund()
    {
        await using var db = Memory();
        var id = await Seed(db);
        var fake = new Gateway();
        var service = Service(db, fake);
        var start = await service.StartAsync(id, "klarna-1");
        Check(start.Status == "Pending" && start.RedirectUrl == "https://pay.klarna.test/session", "pending redirect");
        Check(fake.StartCount == 1 && (await service.StartAsync(id, "klarna-1")).RedirectUrl == start.RedirectUrl && fake.StartCount == 1,
            "same key reuses HPP session");
        await Reject(() => new CheckoutService(db, Finance(db), Config).PayCardAsync(id, "4111111111111111", "12/30", "card-while-klarna"));
        Check(await new CheckoutService(db, Finance(db), Config).ExpirePendingAsync() == 0,
            "pending Klarna does not release reserved stock");
        fake.HppStatus = "COMPLETED";
        var authorized = await service.ReconcileAsync(id);
        Check(authorized.Status == "Authorized" && authorized.PaidAt == null, "authorization is not payment success");
        Check((await db.OrderTables.SingleAsync(x => x.Id == id)).Status == "Paid", "seller can prepare authorized order");
        Check(!await db.SellerSettlements.AnyAsync(), "authorization did not credit seller");
        Check((await service.ReconcileAsync(id)).Status == "Authorized", "reconciliation idempotent");
        var captured = await service.CaptureForShipmentAsync(id);
        Check(captured?.Status == "Succeeded" && captured.PaidAt != null && captured.ProviderTransactionId == "capture-1",
            "full provider capture recorded");
        Check((await service.CaptureForShipmentAsync(id))?.Status == "Succeeded" && fake.CaptureCount == 1,
            "retry does not capture twice");
        Check(await db.SellerSettlements.CountAsync() == 1, "finance ledger credited once only after capture");
        var refund = new RefundGateway(new PayPalReservationChecks.Gateway(), klarna: fake);
        Check(await refund.RefundAsync(captured!, 20m, "refund-1", default) == "refund-1" && fake.RefundCount == 1,
            "partial refund goes to Klarna order, not installment ledger");
    }

    private static async Task RejectProviderMismatch()
    {
        await using var db = Memory();
        var id = await Seed(db);
        var fake = new Gateway { HppStatus = "COMPLETED", WrongAmount = true };
        var service = Service(db, fake);
        await service.StartAsync(id, "wrong-amount");
        await Reject(() => service.ReconcileAsync(id));
        Check((await db.Payments.SingleAsync()).Status == "Pending" && !await db.SellerSettlements.AnyAsync(),
            "mismatched provider money cannot mark paid");
    }

    private static async Task FailedSessionStaysRetryable()
    {
        await using var db = Memory();
        var id = await Seed(db);
        var fake = new Gateway { HppStatus = "FAILED" };
        var service = Service(db, fake);
        await service.StartAsync(id, "klarna-fail");
        Check((await service.ReconcileAsync(id)).Status == "Pending", "provider refusal can retry same HPP session");
        Check((await service.StartAsync(id, "klarna-fail")).RedirectUrl != null && fake.StartCount == 1,
            "same HPP session remains retryable after decline");
    }

    private static async Task CancelledSessionReleasesAttempt()
    {
        await using var db = Memory();
        var id = await Seed(db);
        var fake = new Gateway { HppStatus = "CANCELLED" };
        var service = Service(db, fake);
        await service.StartAsync(id, "klarna-cancelled");
        Check((await service.ReconcileAsync(id)).Status == "Failed", "HPP cancellation is final");
        await service.StartAsync(id, "klarna-new-key");
        Check(fake.StartCount == 2, "new payment key after cancellation");
    }

    private static async Task MaintenanceReconcilesAbandonedSession()
    {
        await using var db = Memory();
        var id = await Seed(db);
        var fake = new Gateway { HppStatus = "COMPLETED" };
        var service = Service(db, fake);
        await service.StartAsync(id, "abandoned-browser");
        await service.MaintainAsync();
        Check((await db.Payments.SingleAsync()).Status == "Authorized", "worker checks HPP without browser return");
        Check(!await db.SellerSettlements.AnyAsync(), "worker does not credit seller before capture");
    }

    private static async Task MaintenanceDisablesExpiredSession()
    {
        await using var db = Memory();
        var id = await Seed(db);
        var fake = new Gateway();
        var service = Service(db, fake);
        await service.StartAsync(id, "expired-hpp");
        var order = await db.OrderTables.SingleAsync(x => x.Id == id);
        order.PaymentExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        await service.MaintainAsync();
        Check(fake.DisableCount == 1 && (await db.Payments.SingleAsync()).Status == "Failed",
            "worker disables HPP before releasing the attempt");
        await new CheckoutService(db, Finance(db), Config).ExpirePendingAsync();
        Check(order.Status == "Expired" && (await db.Inventories.SingleAsync()).Quantity == 1,
            "expired HPP permits ordinary stock release");
    }

    private static async Task RejectMissingUsZip()
    {
        await using var db = Memory();
        var id = await Seed(db);
        var fake = new Gateway();
        var order = await db.OrderTables.SingleAsync(x => x.Id == id);
        order.DeliveryPostalCodeSnapshot = null;
        await db.SaveChangesAsync();
        await Reject(() => Service(db, fake).StartAsync(id, "missing-zip"));
        Check(fake.StartCount == 0 && !await db.Payments.AnyAsync(),
            "server rejects direct Klarna start without US ZIP");
    }

    private static async Task UseProviderHppExpiry()
    {
        await using var db = Memory();
        var id = await Seed(db);
        var fake = new Gateway { ExpiresAt = DateTimeOffset.UtcNow.AddHours(2) };
        await Service(db, fake).StartAsync(id, "hpp-expiry");
        var expiry = (await db.OrderTables.SingleAsync(x => x.Id == id)).PaymentExpiresAt;
        Check(expiry > DateTime.UtcNow.AddHours(2).AddMinutes(4)
            && expiry < DateTime.UtcNow.AddHours(2).AddMinutes(6),
            "local expiry follows provider HPP expiry with disable grace");
    }

    private static async Task VoidUncapturedAuthorization()
    {
        await using var db = Memory();
        var id = await Seed(db);
        var fake = new Gateway { HppStatus = "COMPLETED" };
        var service = Service(db, fake);
        await service.StartAsync(id, "void-authorization");
        await service.ReconcileAsync(id);
        Check(await service.CancelAuthorizationAsync(id), "authorized order can be voided");
        Check(fake.CancelCount == 1 && fake.RefundCount == 0, "void is not a refund");
        Check((await db.Payments.SingleAsync()).Status == "Failed"
            && (await db.Inventories.SingleAsync()).Quantity == 1
            && !await db.SellerSettlements.AnyAsync(), "void restores stock without seller credit");
    }

    private static KlarnaPaymentService Service(ApplicationDbContext db, Gateway fake) =>
        new(db, fake, Config, Finance(db));

    private static SellerFinanceService Finance(ApplicationDbContext db) => new(db, Config);

    private static ApplicationDbContext Memory() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<int> Seed(ApplicationDbContext db)
    {
        var buyer = new User { Role = "buyer", Email = "klarna-buyer-" + Guid.NewGuid() + "@example.test" };
        var seller = new User { Role = "seller", Email = "klarna-seller-" + Guid.NewGuid() + "@example.test" };
        db.Users.AddRange(buyer, seller);
        await db.SaveChangesAsync();
        var address = new Address
        {
            UserId = buyer.Id, FullName = "Demo Buyer", Street = "509 Amsterdam Ave", City = "New York",
            State = "NY", Country = "US", PostalCode = "10024"
        };
        db.Addresses.Add(address);
        db.SellerAccounts.Add(new SellerAccount
        {
            SellerId = seller.Id, Level = 1, MonthlySalesLimit = 10000m,
            SalesMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1), UpdatedAt = DateTime.UtcNow
        });
        var product = new Product { SellerId = seller.Id, Title = "Klarna test product", Price = 100m };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.Inventories.Add(new Inventory { ProductId = product.Id, Quantity = 0 });
        var order = new OrderTable
        {
            BuyerId = buyer.Id, SellerId = seller.Id, AddressId = address.Id,
            Status = "AwaitingPayment", TotalPrice = 100m, Subtotal = 100m, Currency = "USD",
            DeliveryNameSnapshot = address.FullName, DeliveryStreetSnapshot = address.Street,
            DeliveryCitySnapshot = address.City, DeliveryStateSnapshot = address.State,
            DeliveryCountrySnapshot = address.Country, DeliveryPostalCodeSnapshot = address.PostalCode,
            OrderDate = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            PaymentExpiresAt = DateTime.UtcNow.AddMinutes(15), PricingSchemaVersion = 1,
            SellerGrossSnapshot = 100m
        };
        order.OrderItems.Add(new OrderItem
        {
            ProductId = product.Id, Quantity = 1, UnitPrice = 100m, ProductTitleSnapshot = "Klarna test product"
        });
        db.OrderTables.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static void Check(bool okay, string message)
    {
        if (!okay) throw new Exception("Klarna check failed: " + message);
    }

    private static async Task Reject(Func<Task> task)
    {
        try { await task(); } catch (InvalidOperationException) { return; }
        throw new Exception("Expected Klarna rejection");
    }

    internal sealed class Gateway : IKlarnaGateway
    {
        public string HppStatus = "WAITING";
        public bool WrongAmount;
        public DateTimeOffset? ExpiresAt;
        public int StartCount, CaptureCount, RefundCount, DisableCount, CancelCount;
        private long captured;
        public Task<KlarnaSession> StartAsync(KlarnaCheckoutRequest request, CancellationToken ct)
        {
            StartCount++;
            Check(request.OrderAmountMinor == 10000 && request.ShippingAddress.PostalCode == "10024",
                "snapshot, USD cents and US ZIP sent");
            return Task.FromResult(new KlarnaSession("kp-1", "hpp-1",
                "https://api-na.playground.klarna.com/hpp/v1/sessions/hpp-1",
                "https://pay.klarna.test/session", ExpiresAt));
        }
        public Task<KlarnaHppStatus> ReadHppAsync(string url, CancellationToken ct) =>
            Task.FromResult(new KlarnaHppStatus(HppStatus, HppStatus == "COMPLETED" ? "order-1" : null));
        public Task<KlarnaOrderStatus> ReadOrderAsync(string id, CancellationToken ct) =>
            Task.FromResult(new KlarnaOrderStatus("order-1", captured == 0 ? "AUTHORIZED" : "CAPTURED",
                WrongAmount ? 10100 : 10000, captured, "USD", "G4-1", captured == 0 ? null : "capture-1"));
        public Task<KlarnaCapture> CaptureAsync(string id, long amount, string key, CancellationToken ct)
        {
            CaptureCount++;
            captured = amount;
            return Task.FromResult(new KlarnaCapture("capture-1", amount));
        }
        public Task<string> RefundAsync(string id, long amount, string key, CancellationToken ct)
        {
            RefundCount++;
            Check(id == "order-1" && amount == 2000 && key == "refund-1", "refund amount and idempotency");
            return Task.FromResult("refund-1");
        }
        public Task CancelOrderAsync(string id, string key, CancellationToken ct)
        {
            CancelCount++;
            return Task.CompletedTask;
        }
        public Task DisableHppAsync(string url, CancellationToken ct)
        {
            DisableCount++;
            HppStatus = "DISABLED";
            return Task.CompletedTask;
        }
    }
}
