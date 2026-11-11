using Microsoft.EntityFrameworkCore;

/// <summary>Address change with phone/ZIP (review 10/11/2026): one successful change in total.</summary>
internal static class AddressContactChecks
{
    public static async Task RunAsync()
    {
        await PhoneOnlyAsync();
        await StructuredSnapshotAsync();
        Console.WriteLine(
            "Address change contact: phone-only change uses the single change, ZIP/phone/name snapshots applied on approval, canonical region, retry key and validation passed"
        );
    }

    private static AddressChangeInput Input(
        string name,
        string street,
        string city,
        string state,
        string phone,
        string? zip = null,
        string? key = null
    ) =>
        new(
            new(name, street, city, state, "Việt Nam", zip, phone),
            "Đổi người nhận",
            key ?? Guid.NewGuid().ToString("N")
        );

    private static async Task PhoneOnlyAsync()
    {
        var f = await AddressChangeChecks.Fixture("Hà Nội");
        await using var db = f.Db;
        Check(f.Order.DeliveryPhoneSnapshot == "+84901112223", "checkout snapshots the phone");
        var same = Input("Buyer", "Original street", "Hà Nội", "Hà Nội", "0901 112 223");
        await Throws<ArgumentException>(
            () => f.Changes.QuoteAsync(f.Order.Id, 1, same),
            "unchanged address and phone accepted"
        );
        var phoneOnly = same with
        {
            Address = same.Address with { Phone = "0987654321" },
        };
        var quote = await f.Changes.QuoteAsync(f.Order.Id, 1, phoneOnly);
        Check(
            quote.OldShippingCost == quote.NewShippingCost
                && quote.OldPhone == "+84901112223"
                && quote.NewPhone == "+84987654321"
                && quote.ShippingZone == "SameProvince",
            "phone-only quote keeps the route price and shows both numbers"
        );
        var r = await AddressChangeChecks.SendAsync(f.Changes, db, f.Order.Id, 1, phoneOnly);
        Check(
            (await db.OrderTables.AsNoTracking().SingleAsync()).DeliveryPhoneSnapshot
                == "+84901112223",
            "pending request does not change the phone"
        );
        var done = await f.Changes.DecideAsync(r.Id, 2, new(true, null));
        var order = await db.OrderTables.AsNoTracking().SingleAsync();
        Check(
            done.Status == "Approved"
                && order.DeliveryPhoneSnapshot == "+84987654321"
                && order.AddressChangeCount == 1,
            "seller approval without a shipper applies the phone and uses the change"
        );
        await Throws<InvalidOperationException>(
            () =>
                f.Changes.QuoteAsync(
                    f.Order.Id,
                    1,
                    Input("Buyer", "Other street", "Hà Nội", "Hà Nội", "0987654321")
                ),
            "second successful change accepted"
        );
    }

    private static async Task StructuredSnapshotAsync()
    {
        var f = await AddressChangeChecks.Fixture("Hà Nội");
        await using var db = f.Db;
        db.SellerShippingPreferences.Add(new() { SellerId = 2, AddressChangeCap = 5 });
        await db.SaveChangesAsync();
        await Throws<ArgumentException>(
            () =>
                f.Changes.QuoteAsync(
                    f.Order.Id,
                    1,
                    Input("Mai", "5 Nguyễn Huệ", "Phường Sài Gòn", "Atlantis", "0911222333")
                ),
            "province outside the list accepted"
        );
        var input = Input(
            "Mai",
            "5 Nguyễn Huệ",
            "Phường Sài Gòn",
            "TP. Hồ Chí Minh",
            "0911 222 333",
            "70000",
            "contact-key"
        );
        var r = await AddressChangeChecks.SendAsync(f.Changes, db, f.Order.Id, 1, input);
        Check(
            r.NewPhone == "+84911222333"
                && r.NewPostalCode == "70000"
                && r.OldPhone == "+84901112223"
                && r.NewShippingCost == 5.20m
                && r.NewAddress.EndsWith("Hồ Chí Minh, Vietnam"),
            "request stores canonical province, phone and ZIP"
        );
        await Throws<InvalidOperationException>(
            () =>
                f.Changes.RequestAsync(
                    f.Order.Id,
                    1,
                    input with { Address = input.Address with { Phone = "0911222334" } }
                ),
            "same key with another phone accepted"
        );
        await f.Changes.DecideAsync(r.Id, 2, new(true, null));
        var order = await db.OrderTables.AsNoTracking().SingleAsync();
        Check(
            order.DeliveryNameSnapshot == "Mai"
                && order.DeliveryStreetSnapshot == "5 Nguyễn Huệ"
                && order.DeliveryCitySnapshot == "Phường Sài Gòn"
                && order.DeliveryPostalCodeSnapshot == "70000"
                && order.DeliveryPhoneSnapshot == "+84911222333"
                && order.DeliveryStateSnapshot == "Hồ Chí Minh"
                && order.DeliveryCountrySnapshot == "Vietnam",
            "approval updates every structured delivery snapshot"
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private static async Task Throws<T>(Func<Task> work, string message)
        where T : Exception
    {
        try
        {
            await work();
        }
        catch (T)
        {
            return;
        }
        throw new Exception(message);
    }
}
