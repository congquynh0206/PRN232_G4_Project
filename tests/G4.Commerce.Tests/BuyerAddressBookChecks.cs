using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;

internal static class BuyerAddressBookChecks
{
    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        db.Users.AddRange(new User { Id = 1, Role = "buyer" }, new User { Id = 2, Role = "buyer" });
        await db.SaveChangesAsync();
        var book = new BuyerAddressService(db);
        BuyerAddressInput Input(string name, bool preferred = false) =>
            new(name, "1 Road", "Hanoi", "Hanoi", "Vietnam", preferred);
        var first = (await book.CreateAsync(1, Input(" First  Buyer "))).Addresses.Single();
        Check(
            first.FullName == "First Buyer" && first.IsDefault,
            "first address is normalized and default"
        );
        var second = await book.CreateAsync(1, Input("Second", true));
        Check(
            second.Addresses.Count == 2
                && second.Addresses.Count(x => x.IsDefault) == 1
                && second.Addresses.Single(x => x.IsDefault).FullName == "Second",
            "default switch is consistent"
        );
        var secondId = second.AffectedId!.Value;
        await Deny(() => book.UpdateAsync(2, first.Id, Input("Other buyer")));
        await Deny(() => book.SetDefaultAsync(2, first.Id));
        await Deny(() => book.DeleteAsync(2, first.Id));

        var order = new OrderTable
        {
            Id = 100,
            BuyerId = 1,
            SellerId = 9,
            AddressId = first.Id,
            CheckoutKey = "address-book-replay",
            AddressSnapshot = "Original delivery address",
            TotalPrice = 12m,
            ShippingFee = 2m,
            Status = "AwaitingPayment",
        };
        order.OrderItems.Add(new OrderItem { ProductId = 1, Quantity = 1 });
        db.OrderTables.Add(order);
        await db.SaveChangesAsync();
        var request = new CheckoutRequest(
            first.Id,
            new[] { new CartLine(1, 1) },
            null,
            "address-book-replay"
        );
        var checkout = new CheckoutService(db);
        Check(
            (await checkout.CreateOrderAsync(1, request)).Id == 100,
            "identical replay returns the original order"
        );
        await book.UpdateAsync(1, first.Id, Input("Edited buyer address"));
        await book.DeleteAsync(1, first.Id);
        db.ChangeTracker.Clear();
        var historical = await db.OrderTables.SingleAsync(x => x.Id == 100);
        Check(
            historical.AddressId == first.Id
                && historical.AddressSnapshot == "Original delivery address"
                && historical.TotalPrice == 12m
                && historical.ShippingFee == 2m,
            "delete preserves original order reference, address and money"
        );
        var archived = await db.Addresses.SingleAsync(x => x.Id == first.Id);
        Check(
            archived.UserId == null && archived.IsDefault == false,
            "referenced address is archived outside the selectable book"
        );
        Check(
            (await checkout.CreateOrderAsync(1, request)).Id == 100
                && await db.OrderTables.CountAsync() == 1,
            "replay after deletion still returns one original order"
        );
        await Deny(() => book.UpdateAsync(1, first.Id, Input("Restore archived")));
        await Deny(() => book.SetDefaultAsync(1, first.Id));
        try
        {
            await checkout.QuoteAsync(1, request with { CheckoutKey = "new-key" });
            throw new Exception("Archived address accepted for a new order");
        }
        catch (ArgumentException) { }
        try
        {
            await checkout.CreateOrderAsync(1, request with { AddressId = secondId });
            throw new Exception("Changed replay request accepted");
        }
        catch (InvalidOperationException) { }
        var visible = await book.ListAsync(1);
        Check(
            visible.Count == 1 && visible.Single().Id == secondId && visible.Single().IsDefault,
            "archived address cannot reappear in buyer list"
        );
        await book.DeleteAsync(1, secondId);
        Check(
            (await book.ListAsync(1)).Count == 0
                && !await db.Addresses.AnyAsync(x => x.Id == secondId),
            "unused address can be deleted and an empty book has no default"
        );
        var third = await book.CreateAsync(1, Input("Third"));
        await book.CreateAsync(1, Input("Fourth", true));
        var defaultId = (await book.ListAsync(1)).Single(x => x.IsDefault).Id;
        await book.DeleteAsync(1, defaultId);
        Check(
            (await book.ListAsync(1)).Single(x => x.IsDefault).Id == third.AffectedId,
            "deleting default promotes the oldest remaining address"
        );
        Console.WriteLine(
            "Buyer address book: ownership, defaults, archival, order snapshots and idempotent checkout after deletion passed"
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private static async Task Deny<T>(Func<Task<T>> work)
    {
        try
        {
            await work();
        }
        catch (KeyNotFoundException)
        {
            return;
        }
        throw new Exception("Foreign or archived address was accepted");
    }
}
