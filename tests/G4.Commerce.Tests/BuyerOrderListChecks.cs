using G4.Domain.Entities;
using G4.Infrastructure.Orders;
using Microsoft.EntityFrameworkCore;

internal static class BuyerOrderListChecks
{
    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Users.AddRange(new User {Id=1,Username="Buyer",Role="buyer"},
            new User {Id=2,Username="Shop Camera",Role="seller"},new User {Id=3,Role="buyer"});
        db.Products.Add(new Product {Id=1,Title="Camera hiện tại",Images="https://example.test/camera.jpg",SellerId=2});
        string[] statuses = ["AwaitingPayment","Paid","Preparing","Shipping","Delivered","Closed","Cancelled","Expired","CancelRequested"];
        for(var i=0;i<statuses.Length;i++)
            db.OrderTables.Add(new OrderTable {Id=i+1,BuyerId=1,SellerId=2,Status=statuses[i],TotalPrice=191.89m});
        db.OrderTables.Add(new OrderTable {Id=99,BuyerId=3,SellerId=2,Status="Paid"});
        db.OrderItems.AddRange(new OrderItem {Id=1,OrderId=2,ProductId=1,ProductTitleSnapshot="Camera đã mua",Quantity=2},
            new OrderItem {Id=2,OrderId=2,ProductTitleSnapshot="Sản phẩm khác",Quantity=3});
        db.Refunds.Add(new Refund {OrderId=6,Status="Succeeded",Amount=191.89m});
        db.ReturnRequests.Add(new ReturnRequest {OrderId=5,Status="Requested"});
        db.Disputes.Add(new Dispute {OrderId=4,WorkflowEnabled=true,IsOpen=true,Status="AwaitingBuyer"});
        await db.SaveChangesAsync();
        var page = await BuyerOrderList.ReadAsync(db,1,3,1,"ready");
        Check(page.TotalCount==3 && page.Counts["all"]==9,"buyer filters/counts exclude another buyer");
        Check(page.Counts["unpaid"]==1 && page.Counts["complete"]==2 && page.Counts["cancelled"]==2,"status counts cover all pages");
        var card = page.Items.Single();
        Check(card.Id==2 && card.ProductTitle=="Camera đã mua" && card.ImageUrl=="https://example.test/camera.jpg","deterministic product snapshot and live Product.images URL");
        Check(card.SellerName=="Shop Camera" && card.ProductCount==2 && card.ItemCount==5,"buyer card includes seller and item totals");
        var all = await BuyerOrderList.ReadAsync(db,1,1,20,"all");
        Check(all.Items.Single(x=>x.Id==4).Attention=="Cần phản hồi phương án","buyer attention reflects buyer response");
        Check(all.Items.Single(x=>x.Id==5).Attention=="Chờ người bán duyệt trả hàng","return status visible on buyer card");
        Check(all.Items.Single(x=>x.Id==6).HasRefund,"refund remains visible on closed order");
        Check(all.Items.Single(x=>x.Id==9).ImageUrl is null,"legacy order without product has no invented image");
        var empty = await BuyerOrderList.ReadAsync(db,3,99,10,"shipping");
        Check(empty.Page==1 && empty.Items.Count==0,"empty page clamps correctly");
        var pending = await BuyerOrderList.ReadAsync(db,1,1,10,"pending");
        Check(pending.TotalCount==4,"legacy pending filter remains compatible");
        var capture = new CaptureSellerSql();
        await using var sqlDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=unused;Database=translation-test;Integrated Security=true")
            .AddInterceptors(new SuppressSellerSqlConnection(),capture).Options);
        await BuyerOrderList.ReadAsync(sqlDb,1,1,10,"ready");
        Check(capture.Commands==3,"SQL Server translates summary, counts and page queries");
        Console.WriteLine("Buyer order ownership, filters, product images and SQL summaries passed");
    }

    private static void Check(bool condition,string message)
    {
        if(!condition) throw new Exception(message);
    }
}
