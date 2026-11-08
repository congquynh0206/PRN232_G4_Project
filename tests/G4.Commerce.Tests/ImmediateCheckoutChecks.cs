using System.Text.Json;
using System.Security.Claims;
using G4.Api.Controllers;
using G4.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;

internal static class ImmediateCheckoutChecks
{
    internal static ImmediateCheckoutService Service(ApplicationDbContext db) => new(
        db,
        new CheckoutService(db, new SellerFinanceService(db, Config), Config)
    );

    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Mail:Mode"] = "Capture" })
        .Build();

    internal static async Task<(int Product, int Buyer, int Address, int Seller)> Seed(
        ApplicationDbContext db,
        int stock = 5
    )
    {
        var buyer = new User
        {
            Role = "buyer", Email = Guid.NewGuid() + "@example.test", Username = "Immediate buyer",
        };
        var seller = new User
        {
            Role = "seller", Email = Guid.NewGuid() + "@example.test", Username = "Immediate seller",
        };
        db.Users.AddRange(buyer, seller);
        await db.SaveChangesAsync();
        var address = new Address
        {
            UserId = buyer.Id, FullName = "Buyer", Street = "1 QA", City = "Hanoi",
            State = "Hanoi", Country = "Vietnam", IsDefault = true,
        };
        db.Addresses.AddRange(address, new Address
        {
            UserId = seller.Id, FullName = "Seller", Street = "2 QA", City = "Hanoi",
            State = "Hanoi", Country = "Vietnam", IsDefault = true,
        });
        var product = new Product
        {
            Title = "Immediate test", SellerId = seller.Id, Price = 10m,
            WeightKg = 1.2m, ImmediatePaymentRequired = true,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.Inventories.Add(new Inventory { ProductId = product.Id, Quantity = stock });
        await db.SaveChangesAsync();
        return (product.Id, buyer.Id, address.Id, seller.Id);
    }

    internal static async Task<Guid> Start(
        ApplicationDbContext db, int product, int buyer, int address, int quantity = 1
    )
    {
        var result = await Service(db).StartAsync(buyer, new(product, quantity, address, null));
        return JsonSerializer.SerializeToElement(result).GetProperty("Id").GetGuid();
    }

    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options
        );
        var f = await Seed(db);
        var id = await Start(db, f.Product, f.Buyer, f.Address);
        Check(await db.OrderTables.CountAsync() == 0, "opening checkout creates no order");
        Check((await db.Inventories.SingleAsync()).Quantity == 5, "opening checkout holds no stock");
        var service = Service(db);
        await Reject(() => new CheckoutService(db).CreateOrderAsync(f.Buyer,
            new(f.Address, [new(f.Product, 1)], null, "bypass")));
        await Reject(() => service.PayCardAsync(f.Seller, id, new("4111111111111111", "12/30", "owner")));
        await Reject(() => Start(db, f.Product, f.Buyer, f.Address, 6));
        var failed = await service.PayCardAsync(f.Buyer, id,
            new("4000000000000002", "12/30", "decline"));
        Check(failed.Status == "Failed", "decline is persisted");
        Check(await db.OrderTables.CountAsync() == 0, "decline creates no order");
        Check((await db.Inventories.SingleAsync()).Quantity == 5, "decline consumes no inventory");
        Check((await service.PayCardAsync(f.Buyer, id,
            new("4111111111111111", "12/30", "decline"))).Status == "Failed",
            "replaying failed key does not charge even with different card input");
        var paid = await service.PayCardAsync(f.Buyer, id,
            new("4111111111111111", "12/30", "paid"));
        Check(paid.Status == "Succeeded", "payment succeeds");
        Check((await db.OrderTables.SingleAsync()).Status == "Paid", "only Paid order is created");
        Check((await db.OrderTables.SingleAsync()).SellerId == f.Seller, "seller receives paid order");
        Check((await db.OrderTables.SingleAsync()).BuyerId == f.Buyer, "buyer history includes order");
        Check((await db.Payments.SingleAsync()).Status == "Succeeded", "captured simulator transaction");
        Check((await db.SellerSettlements.SingleAsync()).OrderId == paid.OrderId, "finance settlement");
        Check((await db.Inventories.SingleAsync()).Quantity == 4, "exactly one item consumed");
        await service.PayCardAsync(f.Buyer, id, new("4111111111111111", "12/30", "paid"));
        await service.PayCardAsync(f.Buyer, id, new("4111111111111111", "12/30", "new-key"));
        Check(await db.Payments.CountAsync() == 1 && await db.OrderTables.CountAsync() == 1,
            "same and new keys cannot charge a completed session again");
        var second = await Start(db, f.Product, f.Buyer, f.Address);
        await Reject(() => service.PayCardAsync(f.Buyer, second,
            new("4111111111111111", "12/30", "paid")));
        var p = await db.Products.SingleAsync();
        p.Price = 11m;
        await db.SaveChangesAsync();
        await Reject(() => service.PayCardAsync(f.Buyer, second,
            new("4111111111111111", "12/30", "changed-price")));
        p.IsListed = false;
        await db.SaveChangesAsync();
        await Reject(() => service.PayCardAsync(f.Buyer, second,
            new("4111111111111111", "12/30", "unlisted")));
        p.IsListed = true;
        p.ImmediatePaymentRequired = false;
        await db.SaveChangesAsync();
        var normal = await new CheckoutService(db).CreateOrderAsync(f.Buyer,
            new(f.Address, [new(f.Product, 1)], null, "normal-regression"));
        Check(normal.Status == "AwaitingPayment", "normal payment still creates unpaid order");
        var controller = new CatalogController(db, new CheckoutService(db),
            new SellerFinanceService(db, Config),
            new HostingEnvironment { EnvironmentName = "Development" });
        void AsUser(int userId, string role) => controller.ControllerContext = new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Role, role),
                ], "test")),
            },
        };
        AsUser(f.Buyer, "buyer");
        Check(await controller.Settings(f.Product, new(true, true, 11m, 11m), default)
            is StatusCodeResult { StatusCode: 403 }, "buyer cannot edit product payment policy");
        AsUser(f.Buyer, "seller");
        Check(await controller.Settings(f.Product, new(true, true, 11m, 11m), default)
            is NotFoundResult, "seller cannot edit another seller's product");
        AsUser(f.Seller, "seller");
        Check(await controller.Settings(f.Product, new(true, true, 11m, 11m), default)
            is OkObjectResult && p.ImmediatePaymentRequired, "seller config saved");
        var publish = await controller.Publish(
            new("New immediate product", "QA", 5m, 1m, 3, ImmediatePaymentRequired: true), default);
        Check(publish is OkObjectResult && await db.Products.AnyAsync(x =>
            x.Title == "New immediate product" && x.ImmediatePaymentRequired),
            "seller create product persists payment configuration");
        Console.WriteLine("Immediate checkout functional checks passed (InMemory; no concurrency claim).");
    }

    internal static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception("Immediate checkout: " + message);
    }

    internal static async Task Reject(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException
            or KeyNotFoundException) { return; }
        throw new Exception("Expected immediate checkout rejection");
    }
}
