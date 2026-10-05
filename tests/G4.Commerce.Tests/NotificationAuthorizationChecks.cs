using G4.Contracts.Checkout;
using G4.Infrastructure.Diagnostics;
using G4.Infrastructure.Persistence;
using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
public static class NotificationAuthorizationChecks
{
    public static async Task RunAsync()
    {
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.OrderTables.AddRange(new OrderTable { Id=1,BuyerId=11,SellerId=21 },new OrderTable { Id=2,BuyerId=12,SellerId=22 });
        db.NotificationOutbox.AddRange(new NotificationOutbox { Id=1,OrderId=1 },new NotificationOutbox { Id=2,OrderId=2 });
        db.IntegrationLogs.AddRange(new IntegrationLog { Id=1,OrderId=1,Service="Carrier" },new IntegrationLog { Id=2,OrderId=2,Service="Carrier" },new IntegrationLog { Id=3,OrderId=null,Service="PayPal" });
        await db.SaveChangesAsync();
        var queries=new DiagnosticsQueryService(db);
        var buyer=await queries.NotificationsAsync(11,"buyer",new(),default);
        if(buyer.TotalCount!=1 || buyer.Items[0].OrderId!=1)throw new Exception("Buyer list leaked another order");
        if((await queries.NotificationsAsync(11,"buyer",new(OrderId:2),default)).TotalCount!=0)throw new Exception("Order filter bypassed buyer ownership");
        var seller=await queries.LogsAsync(21,"seller",new(),default);
        if(seller.TotalCount!=1 || (await queries.LogsAsync(1,"admin",new(),default)).TotalCount!=3)throw new Exception("Seller log scope or admin unlinked logs wrong");
        try { await queries.NotificationAsync(2,11,"buyer",default); throw new Exception("Detail bypassed ownership"); } catch(KeyNotFoundException) { }
        foreach(var role in new[]{"buyer","shipper"})
        { try { await queries.LogsAsync(11,role,new(),default); throw new Exception("Forbidden role accessed logs"); } catch(UnauthorizedAccessException) { } }
        try { await queries.NotificationsAsync(11,"buyer",new(PageSize:101),default); throw new Exception("Oversized page accepted"); } catch(ArgumentException) { }
        Console.WriteLine("Notification/log ownership, unlinked logs, forbidden roles and query validation passed");
    }
}
