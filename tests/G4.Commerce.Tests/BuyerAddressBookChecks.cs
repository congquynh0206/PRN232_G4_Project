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
            new(name, "1 Road", "Hanoi", "Hanoi", "Vietnam", preferred, Phone: "0912 345 678");
        var first = (await book.CreateAsync(1, Input(" First  Buyer "))).Addresses.Single();
        Check(
            first.FullName == "First Buyer" && first.IsDefault,
            "first address is normalized and default"
        );
        Check(
            first.State == "Hà Nội" && first.Country == "Vietnam" && first.Phone == "+84912345678",
            "province, country and phone are stored in canonical form"
        );
        await RulesAsync(book);
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

    // Country list, province/state list, dial code and ZIP rules (review 10/11/2026).
    private static async Task RulesAsync(BuyerAddressService book)
    {
        static BuyerAddressInput Vn(string state, string? phone, string? zip = null) =>
            new("Lan", "12 Lý Thường Kiệt", "Phường Cửa Nam", state, "Việt Nam", false, zip, phone);
        static BuyerAddressInput Us(string? phone, string? zip) =>
            new("Jane", "1 Main St", "Austin", "Texas", "USA", false, zip, phone);
        void Fails(BuyerAddressInput input, string field, string message) =>
            Check(BuyerAddressService.Validate(input).ContainsKey(field), message);

        Fails(Vn("Hà Nội", "0912345678") with { Country = "Japan" }, "country", "only VN/US");
        Fails(Vn("Atlantis", "0912345678"), "state", "province must be in the list");
        Fails(Vn("Hà Nội", null), "phone", "phone is required");
        Fails(Vn("Hà Nội", "12345"), "phone", "short Vietnamese number rejected");
        Fails(Vn("Hà Nội", "+1 212 555 0123"), "phone", "dial code must follow the country");
        Fails(Vn("Hà Nội", "0912abc678"), "phone", "letters rejected");
        Fails(Vn("Hà Nội", "0912345678", "12"), "postalCode", "Vietnamese postal code format");
        Fails(Us("(512) 555-0123", null), "postalCode", "US address needs a ZIP");
        Fails(Us("+84 912 345 678", "73301"), "phone", "US address needs a +1 number");
        Fails(Us("(112) 555-0123", "73301"), "phone", "NANP area code cannot start with 1");

        var us = (await book.CreateAsync(2, Us("(512) 555-0123", "73301"))).Addresses.Single();
        Check(
            us.State == "TX"
                && us.Country == "United States"
                && us.Phone == "+15125550123"
                && us.PostalCode == "73301",
            "US address stores the state code, country name and +1 number"
        );
        var merged = await book.CreateAsync(2, Vn("Tỉnh Bình Dương", "+84 24 3825 1234"));
        var row = merged.Addresses.Single(x => x.Id == merged.AffectedId);
        Check(
            row.State == "Hồ Chí Minh" && row.Phone == "+842438251234",
            "old province maps to the merged one; landline keeps the area code"
        );
        Check(
            DeliveryAddressRules.Phone("Vietnam", "84912345678") is null
                && DeliveryAddressRules.Phone("United States", "1 212 555 0123") == "+12125550123",
            "dial code without + is not guessed for Vietnam; US leading 1 is accepted"
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
