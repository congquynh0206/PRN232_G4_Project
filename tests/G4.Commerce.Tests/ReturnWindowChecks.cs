using G4.Domain.Entities;
using G4.Infrastructure.Returns;
using G4.Infrastructure.Disputes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class ReturnWindowChecks
{
    internal static IConfiguration Config(int seconds = 300) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Returns:WindowSeconds"] = seconds.ToString(), ["Returns:ReceiptSeconds"] = "45", ["Disputes:SellerResponseSeconds"] = "45" }).Build();
    internal sealed record Fixture(ApplicationDbContext Db, OrderTable Order, AddressConfirmationChecks.Clock Clock,
        SellerFinanceService Finance, ReturnService Returns, ShipmentService Shipping, CheckoutService Checkout);
    internal static async Task<Fixture> Create()
    {
        var f = await AddressChangeChecks.Fixture("Hanoi");
        var clock = new AddressConfirmationChecks.Clock(); var config = Config();
        var finance = new SellerFinanceService(f.Db, config, clock);
        var returns = new ReturnService(f.Db, new ReturnAutomationChecks.Gateway(), new ReturnAutomationChecks.Carrier(), finance, config, clock);
        var shipping = new ShipmentService(f.Db, new ReturnAutomationChecks.Carrier(), config, clock);
        var shipment = await shipping.CreateOutboundAsync(f.Order.Id); await shipping.ClaimAsync(shipment.Id, 3);
        foreach (var status in new[] { "PickedUp", "InTransit", "OutForDelivery", "Delivered" })
            Check(await shipping.RecordEventAsync(shipment.Id, "window-" + status, status, shipperId: 3), "delivery event " + status);
        return new(f.Db, f.Order, clock, finance, returns, shipping, new CheckoutService(f.Db, finance, config, clock: clock));
    }
    public static async Task RunAsync()
    {
        await Boundary(); await TimelyContinuation(); await RejectionAndDispute(); await Legacy();
        Console.WriteLine("RT1: delivery-anchored policy snapshot, 299/300-second quote/request/dispute/close boundary, immutable configuration entitlement, legacy seven days, timely return/appeal protection and separate 45-second receipt passed");
    }
    private static async Task Boundary()
    {
        var f = await Create(); await using var db = f.Db; var start = f.Clock.Utc;
        Check(f.Order.ReturnWindowPolicyVersion == 1 && f.Order.ReturnWindowSeconds == 300 && f.Order.ReturnWindowClosesAt == start.AddSeconds(300), "delivery snapshots demo policy and deadline");
        f.Clock.Utc = start.AddSeconds(299);
        var quote = await f.Returns.QuoteAsync(f.Order.Id, "WrongItem");
        var changedConfig = new ReturnService(db, new ReturnAutomationChecks.Gateway(), new ReturnAutomationChecks.Carrier(), f.Finance, Config(1), f.Clock);
        Check((await changedConfig.QuoteAsync(f.Order.Id, "WrongItem")).ReturnWindowClosesAt == quote.ReturnWindowClosesAt, "changing configuration cannot shorten delivered order rights");
        Check(await f.Checkout.CloseDeliveredOutsideReturnWindowAsync() == 0, "still open at 299 seconds");
        f.Clock.Utc = start.AddSeconds(300);
        await Reject(() => f.Returns.QuoteAsync(f.Order.Id, "WrongItem"));
        await Reject(() => f.Returns.RequestPricedReturnAsync(f.Order.Id, new("WrongItem", "late", "late", quote.PricingFingerprint)));
        await Reject(() => f.Returns.RequestReturnAsync(f.Order.Id, "late legacy API"));
        await Reject(() => new DisputeService(db, f.Finance, f.Returns, Config(), f.Clock).OpenAsync(f.Order.Id, 1, new("late dispute", ["https://example.test/evidence"])));
        Check(await f.Checkout.CloseDeliveredOutsideReturnWindowAsync() == 1 && await f.Checkout.CloseDeliveredOutsideReturnWindowAsync() == 0, "close exactly once at deadline");
        Check(!await db.ReturnRequests.AnyAsync() && !await db.Disputes.AnyAsync(), "late failures leave no return/dispute");
    }
    private static async Task TimelyContinuation()
    {
        var f = await Create(); await using var db = f.Db; var deadline = f.Order.ReturnWindowClosesAt;
        f.Clock.Utc = f.Clock.Utc.AddSeconds(299);
        var quote = await f.Returns.QuoteAsync(f.Order.Id, "WrongItem");
        var input = new G4.Contracts.Checkout.ReturnRequestInput("WrongItem", "timely", "timely", quote.PricingFingerprint);
        var request = await f.Returns.RequestPricedReturnAsync(f.Order.Id, input);
        Check(request.ReturnDeadline == deadline, "return stores deadline from delivery, not request time");
        f.Clock.Utc = f.Clock.Utc.AddSeconds(100);
        Check((await f.Returns.RequestPricedReturnAsync(f.Order.Id, input)).Id == request.Id, "accepted request replays after window closes");
        Check(await f.Checkout.CloseDeliveredOutsideReturnWindowAsync() == 0, "open return survives window");
        await f.Returns.ApproveAsync(request.Id);
        var returned = await db.ShippingInfos.SingleAsync(s => s.OrderId == f.Order.Id && s.Direction == "Return");
        await f.Shipping.ClaimAsync(returned.Id, 3);
        foreach (var status in new[] { "PickedUp", "InTransit", "OutForDelivery", "Delivered" })
            await f.Shipping.RecordEventAsync(returned.Id, "return-" + status, status, shipperId: 3);
        Check(request.ConfirmationDueAt == f.Clock.Utc.AddSeconds(45) && f.Order.ReturnWindowClosesAt == deadline, "return delivery starts receipt only, never resets outbound window");
        f.Clock.Utc = f.Clock.Utc.AddSeconds(45);
        await f.Returns.MaintainAsync();
        Check(request.Status == "Refunded" && await db.Refunds.CountAsync(x => x.Status == "Succeeded") == 1, "timely return finishes after window with normal receipt refund");
    }
    private static async Task RejectionAndDispute()
    {
        var f = await Create(); await using var db = f.Db;
        f.Clock.Utc = f.Clock.Utc.AddSeconds(299);
        var quote = await f.Returns.QuoteAsync(f.Order.Id, "WrongItem");
        var r = await f.Returns.RequestPricedReturnAsync(f.Order.Id, new("WrongItem", "timely", "reject", quote.PricingFingerprint));
        await f.Returns.RejectAsync(r.Id, "Seller disagrees");
        f.Clock.Utc = f.Clock.Utc.AddSeconds(10);
        Check(await f.Checkout.CloseDeliveredOutsideReturnWindowAsync() == 0, "rejection cannot cut independent appeal rights");
        var d = await new DisputeService(db, f.Finance, f.Returns, Config(), f.Clock).OpenAsync(f.Order.Id, 1, new("Appeal timely return", ["https://example.test/evidence"]));
        Check(d.IsOpen && await f.Checkout.CloseDeliveredOutsideReturnWindowAsync() == 0, "timely return appeal and open dispute survive return window");
    }
    private static async Task Legacy()
    {
        var f = await Create(); await using var db = f.Db;
        // Simulate an imported legacy delivery without policy evidence; its entitlement remains seven days.
        f.Order.ReturnWindowClosesAt = null; f.Order.ReturnWindowStartedAt = null; f.Order.ReturnWindowSeconds = null; f.Order.ReturnWindowPolicyVersion = 0;
        await db.SaveChangesAsync();
        f.Clock.Utc = f.Clock.Utc.AddSeconds(600);
        var q = await f.Returns.QuoteAsync(f.Order.Id, "WrongItem");
        var delivered = await db.ShippingInfos.Where(s => s.Direction == "Outbound").Select(s => s.DeliveredAt).SingleAsync();
        Check(q.ReturnWindowClosesAt == delivered!.Value.AddDays(7) && q.ReturnWindowPolicyVersion == 0, "legacy fallback is seven days even with Development config");
        Check(await f.Checkout.CloseDeliveredOutsideReturnWindowAsync() == 0, "legacy not closed by new demo duration");
    }
    internal static void Check(bool value, string message) => ReturnAutomationChecks.Check(value, message);
    internal static Task Reject(Func<Task> work) => ShipmentReattemptChecks.Reject<InvalidOperationException>(work);
}
