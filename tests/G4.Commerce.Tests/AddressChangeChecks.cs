using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class AddressChangeChecks
{
    public static async Task RunAsync()
    {
        await IncreaseAsync();
        await DecreaseAsync();
        await DeliveryRaceAsync();
        await SupportAfterShippingRefundAsync();
        Console.WriteLine("Address changes: cap, ownership, one successful change, stock/payment snapshots, shipper acknowledgement, shipping refunds and final refund passed");
    }

    private static async Task<(ApplicationDbContext Db, CheckoutService Checkout, AddressChangeService Changes, SellerFinanceService Finance, OrderTable Order)> Fixture(string state)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.Users.AddRange(
            new User { Id = 1, Role = "buyer", Email = "buyer@example.test" },
            new User { Id = 2, Role = "seller", Email = "seller@example.test" },
            new User { Id = 3, Role = "shipper" }
        );
        db.Addresses.AddRange(
            new Address { Id = 1, UserId = 1, FullName = "Buyer", Street = "Original street", City = state, State = state, Country = "Vietnam" },
            new Address { Id = 2, UserId = 2, FullName = "Seller", Street = "Pickup street", City = "Hanoi", State = "Hanoi", Country = "Vietnam", IsDefault = true }
        );
        db.Products.Add(new Product { Id = 1, SellerId = 2, Title = "Camera", Price = 100, WeightKg = 1.2m });
        db.Inventories.Add(new Inventory { Id = 1, ProductId = 1, Quantity = 10 });
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Finance:Levels:1:HoldSeconds"] = "1" })
            .Build();
        var finance = new SellerFinanceService(db, config);
        var checkout = new CheckoutService(db, finance);
        var order = await checkout.CreateOrderAsync(1, new(1, [new(1, 1)], null, Guid.NewGuid().ToString()));
        await checkout.PayCardAsync(order.Id, "4111111111111111", "12/30", Guid.NewGuid().ToString());
        return (db, checkout, new AddressChangeService(db, finance), finance, order);
    }

    private static AddressChangeInput Input(string state, string key) => new(new("Buyer", "New street", state, state, "Vietnam"), "Change delivery destination", key);
    private static async Task IncreaseAsync()
    {
        var f = await Fixture("Hanoi");
        await using var db = f.Db;
        var original = f.Order.AddressSnapshot;
        var total = f.Order.TotalPrice;
        var rejected = await f.Changes.RequestAsync(f.Order.Id, 1, Input("HCM", "cap-zero"));
        Check(
            rejected.Status == "Rejected" && f.Order.AddressChangeCount == 0 && f.Order.AddressSnapshot == original,
            "cap rejection leaves order unchanged"
        );
        await Reject(() => f.Changes.RequestAsync(f.Order.Id, 99, Input("HCM", "intruder")), "buyer ownership");
        db.SellerShippingPreferences.Add(new SellerShippingPreference { SellerId = 2, AddressChangeCap = 3.1m });
        await db.SaveChangesAsync();
        var pending = await f.Changes.RequestAsync(f.Order.Id, 1, Input("HCM", "within-cap"));
        Check(
            pending.Status == "Pending" && pending.NewShippingCost == 5.2m && pending.OldShippingCost == 2.1m,
            "fractional ship cost and exact cap"
        );
        Check(
            (await f.Changes.RequestAsync(f.Order.Id, 1, Input("HCM", "within-cap"))).Id == pending.Id,
            "request retry"
        );
        await Reject(
            () => f.Changes.RequestAsync(f.Order.Id, 1, Input("Hanoi", "another-pending")),
            "one pending request"
        );
        await Reject(() => f.Changes.DecideAsync(pending.Id, 99, new(true, null)), "seller ownership");
        var balance = (await db.SellerAccounts.SingleAsync()).ProcessingBalance;
        var approved = await f.Changes.DecideAsync(pending.Id, 2, new(true, null));
        await f.Changes.DecideAsync(pending.Id, 2, new(true, null));
        Check(approved.Status == "Approved" && f.Order.AddressChangeCount == 1, "approval and decision retry");
        Check(
            f.Order.TotalPrice == total && f.Order.ShippingFee == 2.1m && (await db.Addresses.FindAsync(1))!.Street == "Original street",
            "payment and profile snapshots preserved"
        );
        Check(
            (await db.SellerAccounts.SingleAsync()).ProcessingBalance == balance - 3.1m && (await db.SellerSettlements.SingleAsync()).ShippingAdjustmentCost == 3.1m,
            "seller cost charged once"
        );
        Check(
            await db.FinancialTransactions.CountAsync(x => x.Type == "ShippingAdjustmentCost") == 1,
            "one ledger entry"
        );
        await Reject(
            () => f.Changes.RequestAsync(f.Order.Id, 1, Input("Hanoi", "second-success")),
            "only one successful change"
        );
        var shipping = new ShipmentService(db, new Carrier());
        var shipment = await shipping.CreateOutboundAsync(f.Order.Id);
        Check(
            shipment.DeliveryAddressSnapshot == approved.NewAddress && shipment.AddressChangeRequestId == approved.Id,
            "new label uses approved address"
        );
        await shipping.ClaimAsync(shipment.Id, 3);
        await Reject(
            () => shipping.RecordEventAsync(shipment.Id, "unacked", "PickedUp", shipperId: 3),
            "tracking requires acknowledgement"
        );
        await Reject(() => f.Changes.AcknowledgeAsync(shipment.Id, 99), "shipper ownership");
        await f.Changes.AcknowledgeAsync(shipment.Id, 3);
        await f.Changes.AcknowledgeAsync(shipment.Id, 3);
        Check(
            await shipping.RecordEventAsync(shipment.Id, "acked", "PickedUp", shipperId: 3),
            "acknowledged shipper continues"
        );
        Check(
            (await db.Inventories.SingleAsync()).Quantity == 9 && await db.Payments.CountAsync() == 1,
            "address change never reserves stock or charges buyer again"
        );
    }

    private static async Task DecreaseAsync()
    {
        var f = await Fixture("HCM");
        await using var db = f.Db;
        var payment = await db.Payments.SingleAsync();
        var balance = (await db.SellerAccounts.SingleAsync()).ProcessingBalance;
        var request = await f.Changes.RequestAsync(f.Order.Id, 1, Input("Hanoi", "decrease"));
        Check(request.BuyerRefund == 3.1m, "refund based on shipping difference");
        await f.Changes.DecideAsync(request.Id, 2, new(true, null));
        await f.Changes.DecideAsync(request.Id, 2, new(true, null));
        var refund = await db.Refunds.SingleAsync();
        Check(
            refund.Amount == 3.1m && refund.Reason == "shipping-adjustment" && refund.ProviderRefundId!.StartsWith("SIM-SHIP-"),
            "internal simulated refund once"
        );
        var settlement = await db.SellerSettlements.SingleAsync();
        Check(
            settlement.Status == "Processing" && settlement.FeeCreditAmount == 0 && (await db.SellerAccounts.SingleAsync()).ProcessingBalance == balance - 3.1m,
            "shipping refund keeps release schedule and fee snapshots"
        );
        var returns = new ReturnService(db, new Refunds(), new Carrier(), f.Finance);
        await returns.RequestCancelAsync(f.Order.Id);
        await returns.DecideCancelAsync(f.Order.Id, true, null);
        Check(
            await db.Refunds.Where(x => x.Status == "Succeeded").SumAsync(x => x.Amount) == payment.Amount,
            "later cancellation refunds remaining captured amount"
        );
        Check(
            (await db.SellerSettlements.SingleAsync()).Status == "Refunded",
            "final refund after shipping adjustment closes settlement"
        );
    }

    private static async Task DeliveryRaceAsync()
    {
        var f = await Fixture("Hanoi");
        await using var db = f.Db;
        var shipping = new ShipmentService(db, new Carrier());
        var shipment = await shipping.CreateOutboundAsync(f.Order.Id);
        await shipping.ClaimAsync(shipment.Id, 3);
        var request = await f.Changes.RequestAsync(
            f.Order.Id,
            1,
            new(new("Buyer", "Other street", "Hanoi", "Hanoi", "Vietnam"), "Move house", "same-fee")
        );
        await shipping.RecordEventAsync(shipment.Id, "pickup", "PickedUp", shipperId: 3);
        await shipping.RecordEventAsync(shipment.Id, "transit", "InTransit", shipperId: 3);
        await shipping.RecordEventAsync(shipment.Id, "delivery", "OutForDelivery", shipperId: 3);
        Check(
            (await db.AddressChangeRequests.FindAsync(request.Id))!.Status == "Rejected",
            "delivery start expires pending request"
        );
        await Reject(
            () => f.Changes.DecideAsync(request.Id, 2, new(true, null)),
            "cannot approve once delivery started"
        );
        await Reject(
            () => f.Changes.RequestAsync(f.Order.Id, 1, Input("Hanoi", "too-late")),
            "cannot request once delivery started"
        );
    }

    private static async Task SupportAfterShippingRefundAsync()
    {
        var f = await Fixture("HCM");
        await using var db = f.Db;
        var change = await f.Changes.RequestAsync(f.Order.Id, 1, Input("Hanoi", "support-shipping-refund"));
        await f.Changes.DecideAsync(change.Id, 2, new(true, null));
        var shipping = new ShipmentService(db, new Carrier());
        var shipment = await shipping.CreateOutboundAsync(f.Order.Id);
        await shipping.ClaimAsync(shipment.Id, 3);
        await f.Changes.AcknowledgeAsync(shipment.Id, 3);
        foreach (var status in new[]
        {
            "PickedUp",
            "InTransit",
            "OutForDelivery",
            "Delivered"
        }

        )
            await shipping.RecordEventAsync(shipment.Id, "support-" + status, status, shipperId: 3);
        var settlement = await db.SellerSettlements.SingleAsync();
        var proceeds = settlement.ProcessingAmount;
        settlement.ReleaseAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        await f.Finance.ReleaseDueFundsAsync();
        Check(
            (await db.SellerAccounts.SingleAsync()).AvailableBalance == proceeds,
            "shipping refund does not prevent funds release after delivery"
        );
        var config = new ConfigurationBuilder().Build();
        var returns = new ReturnService(db, new Refunds(), new Carrier(), f.Finance);
        var disputes = new G4.Infrastructure.Disputes.DisputeService(db, f.Finance, returns, config, TimeProvider.System);
        var links = new[]
        {
            "https://example.test/evidence"
        };
        var dispute = await disputes.OpenAsync(f.Order.Id, 1, new("Item has a defect", links));
        await disputes.MaintainAsync();
        Check(dispute.IsOpen, "shipping-only refund never auto closes support case");
        await disputes.ProposeAsync(dispute.Id, 2, new("Refund", "Refund remaining payment", links));
        await disputes.RespondAsync(dispute.Id, 1, new(true, "Agree to refund"));
        Check(
            !dispute.IsOpen && await db.Refunds.Where(x => x.Status == "Succeeded").SumAsync(x => x.Amount) == f.Order.TotalPrice,
            "support refunds remaining amount after shipping refund"
        );
    }

    private static void Check(bool condition, string message) => ReturnAutomationChecks.Check(condition, message);
    private static async Task Reject(Func<Task> work, string message)
    {
        try
        {
            await work();
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        throw new Exception(message);
    }

    private sealed class Carrier : ICarrierGateway
    {
        public Task<string> CreateLabelAsync(int id, string direction, string key, CancellationToken ct) => Task.FromResult($"TEST-{id}-{direction}");
    }

    private sealed class Refunds : IRefundGateway
    {
        public Task<string> RefundAsync(Payment payment, decimal amount, string key, CancellationToken ct) => Task.FromResult("REFUND-" + key);
    }
}
