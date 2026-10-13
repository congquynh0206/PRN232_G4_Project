using G4.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class AddressChangeSqlChecks
{
    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("G4_ADDRESS_SQL_TEST_CONNECTION") ?? throw new Exception("QA connection required");
        if (new SqlConnectionStringBuilder(connection).InitialCatalog != "G4_AddressChange_Test_20261013") throw new Exception("Dedicated SQL QA database required");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        var config = new ConfigurationBuilder().Build();
        int buyer, seller, shipper, orderId, requestId, shipmentId;
        AddressChangeInput input;
        await using (var db = new ApplicationDbContext(options))
        {
            buyer = (await db.Users.SingleAsync(x => x.Email == "buyer@example.test")).Id;
            seller = (await db.Users.SingleAsync(x => x.Email == "seller@example.test")).Id;
            shipper = (await db.Users.SingleAsync(x => x.Role == "shipper")).Id;
            var address = await db.Addresses.FirstAsync(x => x.UserId == buyer && x.State == "HCM");
            var product = await db.Products.FirstAsync(x => x.SellerId == seller);
            var finance = new SellerFinanceService(db, config); var checkout = new CheckoutService(db, finance);
            var order = await checkout.CreateOrderAsync(buyer, new(address.Id, [new(product.Id, 1)], null, Guid.NewGuid().ToString()));
            orderId = order.Id;
            await checkout.PayCardAsync(orderId, "4111111111111111", "12/30", Guid.NewGuid().ToString());
            var shipment = await new ShipmentService(db, new Carrier()).CreateOutboundAsync(orderId); shipmentId = shipment.Id;
            await new ShipmentService(db, new Carrier()).ClaimAsync(shipmentId, shipper);
            input = new(new("Buyer", "New Hanoi street", "Hanoi", "Hanoi", "Vietnam"), "SQL address change test", Guid.NewGuid().ToString());
        }
        async Task<AddressChangeView> Request()
        {
            await using var db = new ApplicationDbContext(options);
            return await new AddressChangeService(db, new SellerFinanceService(db, config)).RequestAsync(orderId, buyer, input);
        }
        var requests = await Task.WhenAll(Request(), Request()); requestId = requests[0].Id;
        Check(requests[0].Id == requests[1].Id, "SQL request retry creates one record");
        async Task<AddressChangeView> Approve()
        {
            await using var db = new ApplicationDbContext(options);
            return await new AddressChangeService(db, new SellerFinanceService(db, config)).DecideAsync(requestId, seller, new(true, null));
        }
        await Task.WhenAll(Approve(), Approve());
        await using (var db = new ApplicationDbContext(options))
        {
            var order = await db.OrderTables.SingleAsync(x => x.Id == orderId);
            Check(order.AddressChangeCount == 1 && order.AddressSnapshot!.Contains("New Hanoi street"), "SQL approval updates once");
            Check(await db.Refunds.CountAsync(x => x.OrderId == orderId) == 1, "SQL concurrent approval refunds once");
            var refund = await db.Refunds.SingleAsync(x => x.OrderId == orderId);
            Check(refund.Amount == 3m && refund.ProviderRefundId!.StartsWith("SIM-SHIP-"), "SQL shipping simulation amount");
            Check(await db.FinancialTransactions.CountAsync(x => x.OrderId == orderId && x.Type == "RefundApplied") == 1, "SQL refund ledger once");
            Check((await db.SellerSettlements.SingleAsync(x => x.OrderId == orderId)).Status == "Processing", "SQL retains fund release state");
            var s = await db.ShippingInfos.SingleAsync(x => x.Id == shipmentId);
            Check(s.AddressChangeRequestId == requestId && s.DeliveryAddressSnapshot == order.AddressSnapshot, "SQL updates existing shipment");
            try { await new ShipmentService(db, new Carrier()).RecordEventAsync(shipmentId, Guid.NewGuid().ToString(), "PickedUp", shipperId: shipper); throw new Exception("missing ack was accepted"); }
            catch (InvalidOperationException) { }
        }
        async Task Ack()
        {
            await using var db = new ApplicationDbContext(options);
            await new AddressChangeService(db, new SellerFinanceService(db, config)).AcknowledgeAsync(shipmentId, shipper);
        }
        await Task.WhenAll(Ack(), Ack());
        await using (var db = new ApplicationDbContext(options))
        {
            var shipping = new ShipmentService(db, new Carrier());
            Check(await shipping.RecordEventAsync(shipmentId, Guid.NewGuid().ToString(), "PickedUp", shipperId: shipper), "SQL tracking after ack");
            var finance = new SellerFinanceService(db, config);
            var returns = new ReturnService(db, new Refunds(), new Carrier(), finance);
            // Verify cumulative refunds on a separate unshipped paid order.
            var address = await db.Addresses.FirstAsync(x => x.UserId == buyer && x.State == "HCM");
            var product = await db.Products.FirstAsync(x => x.SellerId == seller);
            var checkout = new CheckoutService(db, finance);
            var other = await checkout.CreateOrderAsync(buyer, new(address.Id, [new(product.Id, 1)], null, Guid.NewGuid().ToString()));
            await checkout.PayCardAsync(other.Id, "4111111111111111", "12/30", Guid.NewGuid().ToString());
            var changes = new AddressChangeService(db, finance);
            var change = await changes.RequestAsync(other.Id, buyer, input with { Key = Guid.NewGuid().ToString() });
            await changes.DecideAsync(change.Id, seller, new(true, null));
            await returns.RequestCancelAsync(other.Id); await returns.DecideCancelAsync(other.Id, true, null);
            Check(await db.Refunds.Where(x => x.OrderId == other.Id && x.Status == "Succeeded").SumAsync(x => x.Amount) == other.TotalPrice, "SQL cumulative refund matches original payment");
        }
        Console.WriteLine("Real SQL address requests/approval/ack concurrency, shipment synchronization and cumulative refunds passed");
    }
    private static void Check(bool condition, string message) => ReturnAutomationChecks.Check(condition, message);
    private sealed class Carrier : ICarrierGateway { public Task<string> CreateLabelAsync(int id, string direction, string key, CancellationToken ct) => Task.FromResult($"QA-{id}-{direction}"); }
    private sealed class Refunds : IRefundGateway { public Task<string> RefundAsync(Payment payment, decimal amount, string key, CancellationToken ct) => Task.FromResult("REFUND-" + key); }
}
