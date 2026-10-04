using G4.Domain.Entities;
using G4.Domain.Rules;
using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;

internal static class ShippingWeightChecks
{
    public static async Task RunAsync()
    {
        Equal(2.10m, OrderPricing.Calculate([new(10m, 2, .6m)], 0, true).Shipping, "local fractional shipping");
        Equal(5.20m, OrderPricing.Calculate([new(10m, 2, .6m)], 0, false).Shipping, "remote fractional shipping");
        Equal(2m, OrderPricing.Calculate([new(10m, 1, .1m)], 0, true).Shipping, "base fee below one kg");
        Equal(5.01m, OrderPricing.Calculate([new(10m, 1, 1.005m)], 0, false).Shipping, "round final fee away from zero");
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var product = new Product { Id = 1, SellerId = 2, Price = 10m, WeightKg = .6m, Title = "Camera" };
        var pickup = new Address { Id = 2, UserId = 2, FullName = "Seller", Street = "2 Seller Street", State = "Hà Nội", Country = "Việt Nam", IsDefault = true };
        var destination = new Address { Id = 1, UserId = 1, FullName = "Buyer", Street = "1 Buyer Street", State = "Hanoi", Country = "Vietnam" };
        db.AddRange(product, pickup, destination, new Inventory { Id = 1, ProductId = 1, Quantity = 100 });
        await db.SaveChangesAsync();
        var service = new CheckoutService(db);
        var request = new CheckoutRequest(1, [new(1, 2)], null, "weight-order");
        var order = await service.CreateOrderAsync(1, request);
        Equal(2.10m, order.ShippingFee, "Hanoi spelling normalized");
        Equal<decimal?>(1.2m, order.TotalWeightKg, "quantity-weight order snapshot");
        Equal<decimal?>(.6m, order.OrderItems.Single().UnitWeightKgSnapshot, "unit-weight snapshot");
        product.WeightKg = 9m; pickup.Street = "Changed pickup"; destination.State = "Ho Chi Minh";
        await db.SaveChangesAsync();
        Equal(2.10m, (await service.CreateOrderAsync(1, request)).ShippingFee, "checkout retry keeps original price");
        ReturnAutomationChecks.Check(order.PickupAddressSnapshot!.Contains("2 Seller Street"), "pickup snapshot survives address edits");
        product.WeightKg = null;
        await db.SaveChangesAsync();
        await Reject(() => service.QuoteAsync(1, request), "missing product weight accepted");
        product.WeightKg = 0m;
        await db.SaveChangesAsync();
        await Reject(() => service.QuoteAsync(1, request), "zero product weight accepted");
        product.WeightKg = .6m;
        pickup.State = "Ho Chi Minh";
        await db.SaveChangesAsync();
        Equal(5.20m, (await service.QuoteAsync(1, request)).Shipping, "same non-Hanoi region uses remote rate");
        Console.WriteLine("Shipping fractional weight, validation and snapshot checks passed");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{message}: {expected} != {actual}");
    }
    private static async Task Reject(Func<Task> action, string message)
    {
        try { await action(); } catch (InvalidOperationException) { return; }
        throw new Exception(message);
    }
}
