using G4.Api.Controllers;
using G4.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

internal static class BuyerTransactionChecks
{
    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var created = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
        var paid = created.AddHours(1);
        var completed = created.AddDays(1);
        db.OrderTables.AddRange(
            new OrderTable { Id = 1, BuyerId = 10, Status = "Closed", OrderDate = created, TotalPrice = 100m },
            new OrderTable { Id = 2, BuyerId = 20, Status = "Paid", OrderDate = created, TotalPrice = 500m });
        db.Payments.AddRange(
            new Payment { Id = 1, OrderId = 1, UserId = 10, Method = "Card", Amount = 100m, Status = "Failed", CreatedAt = created, ErrorCode = "DECLINED" },
            new Payment { Id = 2, OrderId = 1, UserId = 10, Method = "Card", Amount = 100m, Status = "Succeeded", CreatedAt = created.AddMinutes(2), PaidAt = paid, ProviderTransactionId = "CAP-1" },
            new Payment { Id = 3, OrderId = 2, UserId = 20, Method = "PayPal", Amount = 500m, Status = "Succeeded", CreatedAt = created, PaidAt = paid });
        db.Refunds.AddRange(
            new Refund { Id = 1, OrderId = 1, PaymentId = 2, Amount = 10m, Status = "Pending", Reason = "return", CreatedAt = created.AddDays(2), IdempotencyKey = "r1" },
            new Refund { Id = 2, OrderId = 1, PaymentId = 2, Amount = 20m, Status = "Succeeded", Reason = "return", CreatedAt = created.AddDays(2), CompletedAt = completed, ProviderRefundId = "REF-1", IdempotencyKey = "r2" });
        await db.SaveChangesAsync();
        var controller = Create(db, 10, "buyer");
        var result = await controller.BuyerTransactions(pageSize: 1);
        var payload = (dynamic)((OkObjectResult)result).Value!;
        var element = System.Text.Json.JsonSerializer.SerializeToElement(payload);
        Check(element.GetProperty("totalCount").GetInt32() == 4, "failed attempt and refunds appear alongside success");
        Check(element.GetProperty("orderTotals")[0].GetProperty("paid").GetDecimal() == 100m
            && element.GetProperty("orderTotals")[0].GetProperty("refunded").GetDecimal() == 20m
            && element.GetProperty("orderTotals")[0].GetProperty("remaining").GetDecimal() == 80m,
            "lifetime order totals include successful rows only and do not use current page");
        Check(element.GetProperty("periodTotals").GetProperty("paid").GetDecimal() == 100m
            && element.GetProperty("periodTotals").GetProperty("refunded").GetDecimal() == 20m,
            "period totals use successful payment and refund events, scoped to current buyer");
        var outsideWindow = await controller.BuyerTransactions(paidFrom: paid.AddHours(1), paidTo: paid.AddDays(1), completedFrom: completed.AddDays(1));
        var outsideTotals = System.Text.Json.JsonSerializer.SerializeToElement(((OkObjectResult)outsideWindow).Value!).GetProperty("periodTotals");
        Check(outsideTotals.GetProperty("paid").GetDecimal() == 0m && outsideTotals.GetProperty("refunded").GetDecimal() == 0m,
            "period totals use PaidAt and CompletedAt, not CreatedAt fallback");
        Check(element.GetProperty("items").GetArrayLength() == 1, "pagination slices displayed rows only");

        var filtered = await controller.BuyerTransactions(orderId: 1, type: "Payment", status: "Failed");
        var filteredElement = System.Text.Json.JsonSerializer.SerializeToElement(((OkObjectResult)filtered).Value!);
        Check(filteredElement.GetProperty("totalCount").GetInt32() == 1
            && filteredElement.GetProperty("items")[0].GetProperty("Detail").GetString() == "DECLINED", "filters and friendly failure detail source retain failed attempt");
        var noForeign = await controller.BuyerTransactions(orderId: 2);
        Check(System.Text.Json.JsonSerializer.SerializeToElement(((OkObjectResult)noForeign).Value!).GetProperty("totalCount").GetInt32() == 0,
            "another buyer's order is excluded even when its id is requested");
        controller = Create(db, 10, "seller");
        var denied = await controller.BuyerTransactions();
        Check(denied is StatusCodeResult { StatusCode: 403 }, "non-buyer role is denied");
        Check(!db.ChangeTracker.HasChanges(), "history and refresh do not mutate payment/refund data");
        Console.WriteLine("Buyer transactions: retry rows, successful-only totals, refund partial, buyer isolation, filters, pagination and read-only behavior passed");
    }

    private static OrdersController Create(ApplicationDbContext db, int userId, string role)
    {
        var controller = new OrdersController(db, null!, null!, new TestEnvironment());
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role)], "test");
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return controller;
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "test";
        public string WebRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = "";
    }
}
