using G4.Domain.Entities;
using G4.Infrastructure.Orders;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

internal static class SellerOrderListChecks
{
    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Users.Add(new User { Id = 1, Username = "Nguyễn Văn A", Role = "buyer" });
        db.Products.Add(new Product {Id=1,Title="Tai nghe",Images="https://example.test/headphones.jpg",SellerId=2});
        string[] statuses = ["AwaitingPayment", "Paid", "Preparing", "Shipping", "Delivered", "Closed", "Cancelled", "Expired", "CancelRequested"];
        for (var i = 0; i < statuses.Length; i++)
            db.OrderTables.Add(new OrderTable { Id = i + 1, SellerId = 2, BuyerId = 1, Status = statuses[i], TotalPrice = 191.89m });
        db.OrderTables.Add(new OrderTable { Id = 99, SellerId = 3, Status = "Paid" });
        db.OrderItems.AddRange(
            new OrderItem { Id = 1, OrderId = 2, ProductId=1, ProductTitleSnapshot = "Tai nghe", Quantity = 2 },
            new OrderItem { Id = 2, OrderId = 2, ProductTitleSnapshot = "Bàn phím", Quantity = 3 });
        db.ReturnRequests.Add(new ReturnRequest { OrderId = 5, Status = "Requested" });
        db.Refunds.Add(new Refund { OrderId = 6, Status = "Succeeded", Amount = 191.89m });
        db.Disputes.Add(new Dispute { OrderId = 4, WorkflowEnabled = true, IsOpen = true, Status = "AwaitingBuyer" });
        await db.SaveChangesAsync();

        var page = await SellerOrderList.ReadAsync(db, 2, 1, 1, "ready", false);
        Check(3, page.TotalCount, "ready includes pre-shipping cancel approval but excludes unpaid");
        Check(9, page.Counts["all"], "counts include all seller pages but exclude another seller");
        Check(1, page.Counts["unpaid"], "unpaid count");
        Check(2, page.Counts["complete"], "delivered and closed count");
        Check(2, page.Counts["cancelled"], "expired and cancelled count");
        Check(9, page.Items.Single().Id, "newest order first");
        Check(9, page.Counts.Where(x => x.Key != "all").Sum(x => x.Value), "every order belongs to one status group");
        page = await SellerOrderList.ReadAsync(db, 2, 3, 1, "ready", false);
        var card = page.Items.Single();
        Check("Tai nghe", card.ProductTitle, "deterministic first product snapshot");
        Check("https://example.test/headphones.jpg",card.ImageUrl,"seller card image reads Product.images");
        Check("Nguyễn Văn A", card.BuyerName, "buyer summary");
        Check(2, card.ProductCount, "distinct order lines");
        Check(5, card.ItemCount, "total quantities");

        var actionable = await SellerOrderList.ReadAsync(db, 2, 1, 10, "all", true);
        Check("9,5,3,2", string.Join(',', actionable.Items.Select(x => x.Id)), "only seller actions, not a case awaiting buyer");
        Check(4, actionable.Counts["all"], "tab counts respect attention toggle");
        Check(4, actionable.ActionCount, "attention count spans every status");
        var completed = await SellerOrderList.ReadAsync(db, 2, 1, 10, "complete", false);
        Check(true, completed.Items.Single(x => x.Id == 6).HasRefund, "refund remains visible on closed order");
        var empty = await SellerOrderList.ReadAsync(db, 3, 20, 10, "shipping", false);
        Check(1, empty.Page, "empty filtered page is clamped");
        Check(0, empty.Items.Count, "no foreign orders in empty page");
        // Exercise the real SQL Server translator as InMemory cannot detect unsupported LINQ.
        var capture = new CaptureSellerSql();
        await using var sqlDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=unused;Database=translation-test;Integrated Security=true")
            .AddInterceptors(new SuppressSellerSqlConnection(), capture).Options);
        var translated = await SellerOrderList.ReadAsync(sqlDb, 2, 1, 10, "ready", true);
        Check(0, translated.Items.Count, "SQL Server can translate the filtered seller page");
        Check(4, capture.Commands, "all summary, grouping and paging queries compile for SQL Server");
        Console.WriteLine("Seller order filters, counts, ownership and summaries passed");
    }

    private static void Check<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{message}: expected {expected}, got {actual}");
    }
}

internal sealed class SuppressSellerSqlConnection : DbConnectionInterceptor
{
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
        ConnectionEventData eventData, InterceptionResult result, CancellationToken ct = default) =>
        ValueTask.FromResult(InterceptionResult.Suppress());
}

internal sealed class CaptureSellerSql : DbCommandInterceptor
{
    public int Commands { get; private set; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
        Commands++;
        var table = new DataTable();
        table.Columns.Add("Value", typeof(int));
        if (command.CommandText.TrimStart().StartsWith("SELECT COUNT(*)", StringComparison.Ordinal))
            table.Rows.Add(0);
        return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
    }
}
