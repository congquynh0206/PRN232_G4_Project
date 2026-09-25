using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Checkout;

public static class DemoSeed
{
    public static async Task EnsureAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var buyer = await db.Users.SingleOrDefaultAsync(u => u.Email == "demo.buyer@example.test", ct);
        if (buyer is null)
        {
            buyer = new User { Username = "demo_buyer", Email = "demo.buyer@example.test", Role = "buyer" };
            db.Users.Add(buyer);
        }
        var seller = await db.Users.SingleOrDefaultAsync(u => u.Email == "demo.seller@example.test", ct);
        if (seller is null)
        {
            seller = new User { Username = "demo_seller", Email = "demo.seller@example.test", Role = "seller" };
            db.Users.Add(seller);
        }
        await db.SaveChangesAsync(ct);

        if (!await db.Stores.AnyAsync(s => s.SellerId == seller.Id, ct))
            db.Stores.Add(new Store { SellerId = seller.Id, StoreName = "G4 Demo Store", Description = "Checkout and shipping demo" });
        if (!await db.Addresses.AnyAsync(a => a.UserId == buyer.Id && a.State == "Hanoi", ct))
            db.Addresses.Add(new Address { UserId = buyer.Id, FullName = "Demo Buyer", Phone = "0900000000", Street = "1 Demo Street", City = "Hanoi", State = "Hanoi", Country = "Vietnam", IsDefault = true });
        if (!await db.Addresses.AnyAsync(a => a.UserId == buyer.Id && a.State == "HCM", ct))
            db.Addresses.Add(new Address { UserId = buyer.Id, FullName = "Demo Buyer", Phone = "0900000000", Street = "2 Demo Street", City = "Ho Chi Minh City", State = "HCM", Country = "Vietnam", IsDefault = false });

        var samples = new (string Title, decimal Price, string Description)[]
        {
            ("Wireless Headphones", 29.90m, "Bluetooth over-ear headphones"),
            ("Compact Camera", 49.00m, "Travel-friendly digital camera"),
            ("Smart Watch", 35.50m, "Fitness and notification watch"),
            ("Phone Case", 9.99m, "Protective case for smartphone"),
            ("Portable Charger", 19.95m, "Fast charging power bank")
        };
        foreach (var sample in samples)
        {
            var product = await db.Products.SingleOrDefaultAsync(p => p.SellerId == seller.Id && p.Title == sample.Title, ct);
            if (product is null)
            {
                product = new Product { SellerId = seller.Id, Title = sample.Title, Price = sample.Price, Description = sample.Description, IsAuction = false };
                db.Products.Add(product);
                await db.SaveChangesAsync(ct);
            }
            if (!await db.Inventories.AnyAsync(i => i.ProductId == product.Id, ct))
                db.Inventories.Add(new Inventory { ProductId = product.Id, Quantity = 20, LastUpdated = DateTime.UtcNow });
        }
        if (!await db.Coupons.AnyAsync(c => c.Code == "G4SAVE10", ct))
            db.Coupons.Add(new Coupon { Code = "G4SAVE10", DiscountPercent = 10m, StartDate = DateTime.UtcNow.AddYears(-1), EndDate = DateTime.UtcNow.AddYears(1), MaxUsage = 100 });
        await db.SaveChangesAsync(ct);
    }
}
