using System.Text.RegularExpressions;
using G4.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

internal static class AddressConfirmationSqlChecks
{
    private const string QaName = "G4_AddressConfirmation_Test_20261020";
    private static AddressChangeService Service(ApplicationDbContext db, TimeProvider? clock = null) => new(db, new SellerFinanceService(db, AddressConfirmationChecks.Config), AddressConfirmationChecks.Config, clock);
    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("G4_ADDRESS_CONFIRMATION_SQL_CONNECTION") ?? throw new Exception("Dedicated AD1 QA connection required");
        if (new SqlConnectionStringBuilder(connection).InitialCatalog != QaName) throw new Exception("Only " + QaName + " is allowed");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        await Prepare(options, connection);
        foreach (var state in new[] { "Hanoi", "HCM" }) await Flow(options, state);
        await ClaimRace(options); await CapRace(options);
        foreach (var state in new[] { "Hanoi", "HCM" }) await Rollback(options, state);
        await ExpiryRace(options);
        Console.WriteLine("Real SQL AD1 passed: additive migration/legacy preservation, quote/request/seller/shipper concurrency, final-only cost/refund, claim/delivery/expiry races, rollback money/address/outbox/realtime");
    }
    private static async Task Prepare(DbContextOptions<ApplicationDbContext> options, string connection)
    {
        await using var db = new ApplicationDbContext(options);
        var creator = db.Database.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync()) await creator.CreateAsync();
        await using var sql = new SqlConnection(connection); await sql.OpenAsync();
        await using var count = new SqlCommand("SELECT COUNT(*) FROM sys.tables WHERE name <> '__EFMigrationsHistory'", sql);
        if (Convert.ToInt32(await count.ExecuteScalarAsync()) == 0)
        {
            var baseline = Regex.Replace(await File.ReadAllTextAsync("database/baseline.sql"), @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE CloneEbayDB;)\s*$", "");
            foreach (var batch in Regex.Split(baseline, @"(?im)^\s*GO\s*$"))
                if (!string.IsNullOrWhiteSpace(batch)) { await using var cmd = new SqlCommand(batch, sql); await cmd.ExecuteNonQueryAsync(); }
            await db.Database.GetService<IMigrator>().MigrateAsync("20261020150408_AddShipmentReattemptSchedulesAndActors");
            await using var legacy = new SqlCommand(@"
                INSERT INTO OrderTable ([status], AddressSnapshot, AddressChangeCount) VALUES (N'Paid', N'Legacy new', 1);
                DECLARE @order int = SCOPE_IDENTITY();
                INSERT INTO AddressChangeRequest (OrderId, RequestKey, Reason, OldAddress, NewAddress, NewState, NewCountry, OldShippingCost, NewShippingCost, SellerCap, BuyerRefund, Status, CreatedAt, DecidedAt)
                VALUES (@order, N'ad1-old-approved', N'Legacy reason', N'Legacy old', N'Legacy new', N'Hanoi', N'Vietnam', 5.2, 2.1, 0, 3.1, N'Approved', '2026-10-18', '2026-10-19');
                INSERT INTO OrderTable ([status]) VALUES (N'Paid'); SET @order = SCOPE_IDENTITY();
                INSERT INTO AddressChangeRequest (OrderId, RequestKey, Reason, OldAddress, NewAddress, NewState, NewCountry, OldShippingCost, NewShippingCost, SellerCap, BuyerRefund, Status, CreatedAt)
                VALUES (@order, N'ad1-old-pending', N'Legacy pending', N'Old', N'New', N'Hanoi', N'Vietnam', 5.2, 2.1, 0, 3.1, N'Pending', '2026-10-18');", sql);
            await legacy.ExecuteNonQueryAsync();
        }
        await db.Database.MigrateAsync();
        var old = await db.AddressChangeRequests.AsNoTracking().SingleAsync(x => x.RequestKey == "ad1-old-approved");
        Check(old.Status == "Approved" && old.WorkflowVersion == 0 && old.BuyerRefund == 3.1m && old.NewAddress == "Legacy new"
            && old.SellerConfirmedAt == null && old.AppliedAt == null && old.BuyerUserId == null && old.DecidedAt == new DateTime(2026,10,19), "legacy result/actors/money/timestamps preserved");
        var pending = await db.AddressChangeRequests.AsNoTracking().SingleAsync(x => x.RequestKey == "ad1-old-pending");
        Check(pending.ExpiresAt == new DateTime(2026,10,19), "legacy pending deadline derives from historical creation without invented approval");
    }
    private sealed record Fixture(int Order, int Buyer, int Seller, int Shipper, int Shipment, string OldAddress);
    private static async Task<Fixture> Seed(DbContextOptions<ApplicationDbContext> options, string state = "HCM", bool claim = true)
    {
        await using var db = new ApplicationDbContext(options); var token = Guid.NewGuid().ToString("N");
        var buyer = new User { Role = "buyer", Email = token + "-buyer@example.test" }; var seller = new User { Role = "seller", Email = token + "-seller@example.test" };
        var shipper = new User { Role = "shipper", Email = token + "-shipper@example.test" };
        db.Users.AddRange(buyer,seller,shipper); await db.SaveChangesAsync();
        var a = new Address { UserId = buyer.Id, FullName = "Buyer", Street = "Original", City = state, State = state, Country = "Vietnam" };
        var pickup = new Address { UserId = seller.Id, FullName = "Seller", Street = "Pickup", City = "Hanoi", State = "Hanoi", Country = "Vietnam", IsDefault = true };
        var product = new Product { SellerId = seller.Id, Title = "AD1", Price = 100, WeightKg = 1.2m };
        db.Addresses.AddRange(a,pickup); db.Products.Add(product); db.SellerShippingPreferences.Add(new() { SellerId = seller.Id, AddressChangeCap = 5 }); await db.SaveChangesAsync();
        db.Inventories.Add(new() { ProductId = product.Id, Quantity = 10 }); await db.SaveChangesAsync();
        var checkout = new CheckoutService(db, new SellerFinanceService(db, AddressConfirmationChecks.Config));
        var order = await checkout.CreateOrderAsync(buyer.Id, new(a.Id, [new(product.Id,1)], null, token));
        await checkout.PayCardAsync(order.Id,"4111111111111111","12/30", token);
        var shipping = new ShipmentService(db,new ReturnAutomationChecks.Carrier()); var s = await shipping.CreateOutboundAsync(order.Id);
        if (claim) await shipping.ClaimAsync(s.Id, shipper.Id);
        return new(order.Id,buyer.Id,seller.Id,shipper.Id,s.Id,order.AddressSnapshot!);
    }
    private static async Task Flow(DbContextOptions<ApplicationDbContext> options, string state)
    {
        var f = await Seed(options,state); var input = AddressConfirmationChecks.Input(state == "HCM" ? "Hanoi" : "HCM");
        await using (var db = new ApplicationDbContext(options)) input = input with { QuoteToken = (await Service(db).QuoteAsync(f.Order,f.Buyer,input)).Token };
        async Task<AddressChangeView> Send() { await using var db = new ApplicationDbContext(options); return await Service(db).RequestAsync(f.Order,f.Buyer,input); }
        var sent = await Task.WhenAll(Send(),Send()); var id = sent[0].Id; Check(id == sent[1].Id, "SQL concurrent request replay creates once");
        async Task<AddressChangeView> Seller() { await using var db = new ApplicationDbContext(options); return await Service(db).DecideAsync(id,f.Seller,new(true,null)); }
        await Task.WhenAll(Seller(),Seller());
        await using (var db = new ApplicationDbContext(options))
        {
            Check((await db.AddressChangeRequests.SingleAsync(x => x.Id == id)).Status == "AwaitingShipper", "SQL seller waits for shipper");
            Check((await db.OrderTables.SingleAsync(x => x.Id == f.Order)).AddressSnapshot == f.OldAddress && !await db.Refunds.AnyAsync(x=>x.OrderId==f.Order)
                && !await db.FinancialTransactions.AnyAsync(x=>x.OrderId==f.Order && x.Type=="ShippingAdjustmentCost"), "SQL no early address/refund/cost");
            var shipping = new ShipmentService(db,new ReturnAutomationChecks.Carrier());
            await shipping.RecordEventAsync(f.Shipment,"ad1-pickup-"+id,"PickedUp",shipperId:f.Shipper);
            await shipping.RecordEventAsync(f.Shipment,"ad1-hub-"+id,"InTransit",shipperId:f.Shipper);
        }
        async Task<AddressChangeView> Shipper() { await using var db = new ApplicationDbContext(options); return await Service(db).DecideShipperAsync(f.Shipment,f.Shipper,new(id,true,null)); }
        async Task Start() { await using var db = new ApplicationDbContext(options); try { await new ShipmentService(db,new ReturnAutomationChecks.Carrier()).RecordEventAsync(f.Shipment,"ad1-start-"+id,"OutForDelivery",shipperId:f.Shipper); } catch(InvalidOperationException) { } }
        await Task.WhenAll(Shipper(),Shipper(),Start());
        await using var verify = new ApplicationDbContext(options);
        var r = await verify.AddressChangeRequests.SingleAsync(x=>x.Id==id); var order = await verify.OrderTables.SingleAsync(x=>x.Id==f.Order);
        Check(r.Status == "Approved" && !r.IsPending && r.ShipperUserId == f.Shipper && order.AddressChangeCount == 1 && order.AddressSnapshot == r.NewAddress, "SQL final confirmation applies once");
        Check(state == "HCM" ? await verify.Refunds.CountAsync(x=>x.OrderId==f.Order)==1 : (await verify.SellerSettlements.SingleAsync(x=>x.OrderId==f.Order)).ShippingAdjustmentCost==3.1m, "SQL one refund/cost after concurrent decisions");
        Check(await verify.NotificationOutbox.CountAsync(x=>x.OrderId==f.Order && x.EventType=="AddressChangeApproved" && x.EventKey!=null)==2, "SQL final mail once per buyer/seller");
        Check((await verify.ShippingInfos.SingleAsync(x=>x.Id==f.Shipment)).DeliveryAddressSnapshot == r.NewAddress, "delivery race uses effective new address");
    }
    private static async Task ClaimRace(DbContextOptions<ApplicationDbContext> options)
    {
        var f = await Seed(options,claim:false); int id;
        await using (var db = new ApplicationDbContext(options)) id=(await AddressChangeChecks.SendAsync(Service(db),db,f.Order,f.Buyer,AddressConfirmationChecks.Input())).Id;
        async Task Seller() { await using var db=new ApplicationDbContext(options); await Service(db).DecideAsync(id,f.Seller,new(true,null)); }
        async Task Claim() { await using var db=new ApplicationDbContext(options); await new ShipmentService(db,new ReturnAutomationChecks.Carrier()).ClaimAsync(f.Shipment,f.Shipper); }
        await Task.WhenAll(Seller(),Claim());
        await using var verify=new ApplicationDbContext(options); var r=await verify.AddressChangeRequests.SingleAsync(x=>x.Id==id); var s=await verify.ShippingInfos.SingleAsync(x=>x.Id==f.Shipment);
        Check(r.Status is "Approved" or "Rejected", "claim/seller race is serialized and never leaves a stale approval pending");
        Check(r.Status == "Approved" ? s.DeliveryAddressSnapshot==r.NewAddress && s.AddressChangeAcknowledgedAt!=null : s.DeliveryAddressSnapshot==f.OldAddress, "unassigned final approval needs no later shipper acknowledgement");
    }
    private static async Task CapRace(DbContextOptions<ApplicationDbContext> options)
    {
        var f = await Seed(options); int id;
        await using (var db = new ApplicationDbContext(options))
        { id = (await AddressChangeChecks.SendAsync(Service(db), db, f.Order, f.Buyer, AddressConfirmationChecks.Input())).Id; await Service(db).DecideAsync(id, f.Seller, new(true, null)); }
        await using var settings = new ApplicationDbContext(options);
        await using var gate = await G4.Infrastructure.Promotions.PromotionLifecycle.LockOrderAsync(settings, 0, default);
        var pref = await settings.SellerShippingPreferences.SingleAsync(x => x.SellerId == f.Seller);
        pref.AddressChangeCap = 10; pref.Revision++; await settings.SaveChangesAsync();
        async Task<AddressChangeView> Final()
        { await using var db = new ApplicationDbContext(options); return await Service(db).DecideShipperAsync(f.Shipment, f.Shipper, new(id, true, null)); }
        var approve = Final();
        await gate!.CommitAsync();
        Check((await approve).Status == "Rejected", "concurrent cap update invalidates confirmation even when new cap still covers increase");
        await using var verify = new ApplicationDbContext(options);
        Check((await verify.OrderTables.SingleAsync(x => x.Id == f.Order)).AddressChangeCount == 0 && !await verify.Refunds.AnyAsync(x => x.OrderId == f.Order), "cap race never applies stale quote");
    }
    private static async Task Rollback(DbContextOptions<ApplicationDbContext> options, string state)
    {
        var f=await Seed(options, state); int id; decimal balance; int signals;
        await using (var db=new ApplicationDbContext(options))
        { id=(await AddressChangeChecks.SendAsync(Service(db),db,f.Order,f.Buyer,AddressConfirmationChecks.Input(state == "HCM" ? "Hanoi" : "HCM"))).Id; await Service(db).DecideAsync(id,f.Seller,new(true,null)); balance=(await db.SellerAccounts.SingleAsync(x=>x.SellerId==f.Seller)).ProcessingBalance; signals=await db.CommerceChanges.CountAsync(); }
        var failing=new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(new FailAfterRefund()).Options;
        await using (var db=new ApplicationDbContext(failing)) await AddressConfirmationChecks.Reject(()=>Service(db).DecideShipperAsync(f.Shipment,f.Shipper,new(id,true,null)));
        await using var verify=new ApplicationDbContext(options);
        Check((await verify.OrderTables.SingleAsync(x=>x.Id==f.Order)).AddressSnapshot==f.OldAddress && (await verify.OrderTables.SingleAsync(x=>x.Id==f.Order)).AddressChangeCount==0, "failed final approval rolls address/count back");
        Check((await verify.AddressChangeRequests.SingleAsync(x=>x.Id==id)).Status=="AwaitingShipper" && !await verify.Refunds.AnyAsync(x=>x.OrderId==f.Order)
            && !await verify.FinancialTransactions.AnyAsync(x=>x.OrderId==f.Order && (x.Type=="RefundApplied" || x.Type=="ShippingAdjustmentCost")) && (await verify.SellerAccounts.SingleAsync(x=>x.SellerId==f.Seller)).ProcessingBalance==balance, "failed final approval rolls money/ledger/request back");
        Check(await verify.CommerceChanges.CountAsync()==signals && !await verify.NotificationOutbox.AnyAsync(x=>x.OrderId==f.Order && x.EventType=="AddressChangeApproved"), "rollback emits no final email/realtime");
        await Service(verify).DecideShipperAsync(f.Shipment,f.Shipper,new(id,true,null));
        Check(state == "HCM" ? await verify.Refunds.CountAsync(x=>x.OrderId==f.Order)==1 : (await verify.SellerSettlements.SingleAsync(x=>x.OrderId==f.Order)).ShippingAdjustmentCost==3.1m,"retry after rollback applies once");
    }
    private static async Task ExpiryRace(DbContextOptions<ApplicationDbContext> options)
    {
        var f=await Seed(options); var clock=new AddressConfirmationChecks.Clock(); int id;
        await using(var db=new ApplicationDbContext(options)) { id=(await AddressChangeChecks.SendAsync(Service(db,clock),db,f.Order,f.Buyer,AddressConfirmationChecks.Input())).Id; await Service(db,clock).DecideAsync(id,f.Seller,new(true,null)); }
        clock.Utc=clock.Utc.AddSeconds(60);
        async Task Worker() { await using var db=new ApplicationDbContext(options); await Service(db,clock).MaintainAsync(); }
        async Task Approve() { await using var db=new ApplicationDbContext(options); await Service(db,clock).DecideShipperAsync(f.Shipment,f.Shipper,new(id,true,null)); }
        await Task.WhenAll(Worker(),Approve());
        await using var verify=new ApplicationDbContext(options);
        Check((await verify.AddressChangeRequests.SingleAsync(x=>x.Id==id)).Status=="Expired" && (await verify.OrderTables.SingleAsync(x=>x.Id==f.Order)).AddressChangeCount==0 && !await verify.Refunds.AnyAsync(x=>x.OrderId==f.Order),"expiry wins at boundary without money or successful change");
    }
    private sealed class FailAfterRefund : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData d,int result,CancellationToken ct=default)
        { if(d.Context!.ChangeTracker.Entries<FinancialTransaction>().Any(x=>x.Entity.Type is "RefundApplied" or "ShippingAdjustmentCost")) throw new InvalidOperationException("Simulated failure after finance ledger save, before commit"); return ValueTask.FromResult(result); }
    }
    private static void Check(bool value,string message)=>AddressConfirmationChecks.Check(value,message);
}
