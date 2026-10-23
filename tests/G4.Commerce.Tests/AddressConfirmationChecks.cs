using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class AddressConfirmationChecks
{
    internal sealed class Clock : TimeProvider
    {
        public DateTime Utc = DateTime.UtcNow;

        public override DateTimeOffset GetUtcNow() => new(Utc);
    }

    internal static IConfiguration Config =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["AddressChanges:ConfirmationSeconds"] = "120" }
            )
            .Build();

    internal static AddressChangeInput Input(string state = "Hanoi") =>
        new(
            new("Buyer", "AD1 new street", state, state, "Vietnam"),
            "Recipient moved",
            Guid.NewGuid().ToString("N")
        );

    public static async Task RunAsync()
    {
        foreach (var state in new[] { "Hanoi", "HCM" })
            await AssignedFlow(state);
        foreach (var stage in new[] { "Pending", "AwaitingShipper" })
            await Timeout(stage);
        foreach (var change in new[] { "cap", "shipper", "refund", "address" })
            await Stale(change);
        await LegacyAndFreeShipping();
        Console.WriteLine(
            "AD1: quote confirmation, assigned/unassigned approval, no early money/address changes, refusal/timeout, stale cap/shipper/quote, free shipping, legacy and idempotency passed"
        );
    }

    private static async Task AssignedFlow(string state)
    {
        var f = await AddressChangeChecks.Fixture(state);
        await using var db = f.Db;
        db.SellerShippingPreferences.Add(new() { SellerId = 2, AddressChangeCap = 5 });
        await db.SaveChangesAsync();
        var clock = new Clock();
        var changes = new AddressChangeService(db, f.Finance, Config, clock);
        var shipping = new ShipmentService(db, new ReturnAutomationChecks.Carrier(), clock: clock);
        var s = await shipping.CreateOutboundAsync(f.Order.Id);
        await shipping.ClaimAsync(s.Id, 3);
        var input = Input(state == "Hanoi" ? "HCM" : "Hanoi");
        await Reject(() => changes.RequestAsync(f.Order.Id, 1, input));
        var quote = await changes.QuoteAsync(f.Order.Id, 1, input);
        input = input with { QuoteToken = quote.Token };
        var r = await changes.RequestAsync(f.Order.Id, 1, input);
        var old = f.Order.AddressSnapshot;
        var ledger = await db.FinancialTransactions.CountAsync();
        var balance = (await db.SellerAccounts.SingleAsync()).ProcessingBalance;
        var wait = await changes.DecideAsync(r.Id, 2, new(true, null));
        Check(
            wait.Status == "AwaitingShipper"
                && wait.SellerUserId == 2
                && wait.BuyerUserId == 1
                && wait.ExpiresAt == clock.Utc.AddSeconds(120),
            "server confirmation audit and fixed deadline"
        );
        Check(
            f.Order.AddressSnapshot == old
                && s.DeliveryAddressSnapshot == old
                && f.Order.AddressChangeCount == 0
                && await db.FinancialTransactions.CountAsync() == ledger
                && !await db.Refunds.AnyAsync()
                && (await db.SellerAccounts.SingleAsync()).ProcessingBalance == balance,
            "seller acceptance cannot change address, money or count"
        );
        Check(
            (await changes.RequestAsync(f.Order.Id, 1, input)).Id == r.Id,
            "request replay while awaiting shipper"
        );
        await changes.DecideAsync(r.Id, 2, new(true, null));
        await Reject(() => changes.DecideShipperAsync(s.Id, 99, new(r.Id, true, null)));
        await Reject(() => changes.AcknowledgeAsync(s.Id, 3));
        await shipping.RecordEventAsync(s.Id, "pickup", "PickedUp", shipperId: 3);
        await shipping.RecordEventAsync(s.Id, "hub", "InTransit", shipperId: 3);
        await Reject(() =>
            shipping.RecordEventAsync(s.Id, "blocked", "OutForDelivery", shipperId: 3)
        );
        var applied = await changes.DecideShipperAsync(s.Id, 3, new(r.Id, true, null));
        var after = await db.FinancialTransactions.CountAsync();
        var mails = await db.NotificationOutbox.CountAsync();
        await changes.DecideShipperAsync(s.Id, 3, new(r.Id, true, null));
        Check(
            applied.Status == "Approved"
                && applied.ShipperUserId == 3
                && applied.AppliedAt == clock.Utc
                && f.Order.AddressChangeCount == 1
                && s.DeliveryAddressSnapshot == applied.NewAddress
                && f.Order.AddressSnapshot == applied.NewAddress,
            "last approver applies atomically"
        );
        Check(
            await db.FinancialTransactions.CountAsync() == after
                && await db.NotificationOutbox.CountAsync() == mails,
            "decision retry cannot repeat money or email"
        );
        Check(
            state == "HCM"
                ? await db.Refunds.CountAsync() == 1
                : (await db.SellerSettlements.SingleAsync()).ShippingAdjustmentCost == 3.1m,
            "refund/cost appears only after final confirmation"
        );
        Check(
            await shipping.RecordEventAsync(s.Id, "start", "OutForDelivery", shipperId: 3),
            "delivery starts after confirmation"
        );
        await Reject(() => changes.QuoteAsync(f.Order.Id, 1, Input()));
    }

    private static async Task Timeout(string stage)
    {
        var f = await AddressChangeChecks.Fixture("HCM");
        await using var db = f.Db;
        var clock = new Clock();
        var changes = new AddressChangeService(db, f.Finance, Config, clock);
        var shipping = new ShipmentService(db, new ReturnAutomationChecks.Carrier(), clock: clock);
        var s = await shipping.CreateOutboundAsync(f.Order.Id);
        await shipping.ClaimAsync(s.Id, 3);
        var r = await AddressChangeChecks.SendAsync(changes, db, f.Order.Id, 1, Input());
        if (stage == "AwaitingShipper")
            await changes.DecideAsync(r.Id, 2, new(true, null));
        clock.Utc = clock.Utc.AddSeconds(119);
        await changes.MaintainAsync();
        Check(
            AddressChangeService.Pending((await db.AddressChangeRequests.FindAsync(r.Id))!),
            "still pending one second before expiry"
        );
        clock.Utc = clock.Utc.AddSeconds(1);
        await changes.MaintainAsync();
        await changes.MaintainAsync();
        Check(
            (await db.AddressChangeRequests.FindAsync(r.Id))!.Status == "Expired"
                && f.Order.AddressChangeCount == 0
                && !await db.Refunds.AnyAsync(),
            "expiry at exact boundary keeps address/money/count"
        );
        var late = await changes.DecideAsync(r.Id, 2, new(true, null));
        Check(late.Status == "Expired", "late retry cannot resurrect expired request");
        var next = await AddressChangeChecks.SendAsync(changes, db, f.Order.Id, 1, Input());
        await changes.DecideAsync(next.Id, 2, new(true, null));
        var refused = await changes.DecideShipperAsync(
            s.Id,
            3,
            new(next.Id, false, "Outside service area")
        );
        Check(
            refused.Status == "Rejected"
                && refused.ShipperUserId == 3
                && f.Order.AddressChangeCount == 0,
            "shipper refusal does not consume successful change"
        );
    }

    private static async Task Stale(string change)
    {
        var f = await AddressChangeChecks.Fixture("HCM");
        await using var db = f.Db;
        var shipping = new ShipmentService(db, new ReturnAutomationChecks.Carrier());
        var s = await shipping.CreateOutboundAsync(f.Order.Id);
        await shipping.ClaimAsync(s.Id, 3);
        var r = await AddressChangeChecks.SendAsync(f.Changes, db, f.Order.Id, 1, Input());
        await f.Changes.DecideAsync(r.Id, 2, new(true, null));
        if (change == "cap")
            db.SellerShippingPreferences.Add(
                new()
                {
                    SellerId = 2,
                    AddressChangeCap = 10,
                    Revision = 1,
                }
            );
        if (change == "shipper")
        {
            db.Users.Add(new() { Id = 4, Role = "shipper" });
            s.ShipperId = 4;
        }
        if (change == "refund")
            db.Refunds.Add(
                new()
                {
                    OrderId = f.Order.Id,
                    PaymentId = (await db.Payments.SingleAsync()).Id,
                    Status = "Succeeded",
                    Amount = 1,
                }
            );
        if (change == "address")
            f.Order.AddressSnapshot += " changed";
        await db.SaveChangesAsync();
        var result = await f.Changes.DecideShipperAsync(
            s.Id,
            change == "shipper" ? 4 : 3,
            new(r.Id, true, null)
        );
        Check(
            result.Status == "Rejected"
                && f.Order.AddressChangeCount == 0
                && result.AppliedAt == null,
            "stale " + change + " cannot reuse confirmations"
        );
    }

    private static async Task LegacyAndFreeShipping()
    {
        var f = await AddressChangeChecks.Fixture("HCM", freeShipping: true);
        await using var db = f.Db;
        Check(
            f.Order.ShippingFee == 0 && f.Order.ShippingDiscount > 0,
            "checkout applied existing free-shipping promotion"
        );
        var r = await AddressChangeChecks.SendAsync(f.Changes, db, f.Order.Id, 1, Input());
        Check(
            r.OldShippingCost > r.NewShippingCost && r.BuyerRefund == 0,
            "carrier savings cannot refund shipping buyer did not pay"
        );
        await f.Changes.DecideAsync(r.Id, 2, new(true, null));
        Check(!await db.Refunds.AnyAsync(), "free shipping creates no simulated refund");
        var legacy = new AddressChangeRequest
        {
            OrderId = f.Order.Id,
            RequestKey = "old",
            Status = "Approved",
            OldAddress = "old",
            NewAddress = f.Order.AddressSnapshot!,
            BuyerRefund = 1,
            CreatedAt = DateTime.UtcNow,
        };
        db.AddressChangeRequests.Add(legacy);
        await db.SaveChangesAsync();
        var count = f.Order.AddressChangeCount;
        await f.Changes.DecideAsync(legacy.Id, 2, new(true, null));
        Check(
            f.Order.AddressChangeCount == count
                && !await db.Refunds.AnyAsync()
                && legacy.SellerUserId == null,
            "historical approved result is not reapplied or assigned invented actors"
        );
    }

    internal static void Check(bool value, string message) =>
        ReturnAutomationChecks.Check(value, message);

    internal static Task Reject(Func<Task> work) => ShipmentReattemptChecks.Reject<Exception>(work);
}
