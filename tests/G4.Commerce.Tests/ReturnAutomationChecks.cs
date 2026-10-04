using G4.Domain.Entities;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Returns;
using G4.Infrastructure.Disputes;
using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Diagnostics;

internal static class ReturnAutomationChecks
{
    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.AddRange(new User { Id = 1, Role = "buyer", Email = "buyer@example.test" },
            new User { Id = 2, Role = "seller", Email = "seller@example.test" },
            new OrderTable { Id = 1, BuyerId = 1, SellerId = 2, Status = "Delivered", TotalPrice = 100m },
            new Payment { Id = 1, OrderId = 1, UserId = 1, Status = "Succeeded", Amount = 100m, Method = "Card" },
            new ShippingInfo { OrderId = 1, Direction = "Outbound", Status = "Delivered", DeliveredAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var finance = new SellerFinanceService(db, new ConfigurationBuilder().Build());
        await finance.RecordSuccessfulPaymentAsync(1, 1);
        var gateway = new Gateway();
        var service = new ReturnService(db, gateway, new Carrier(), finance);
        var request = await service.RequestReturnAsync(1, "Hàng bị hỏng");
        await service.ApproveAsync(request.Id);
        Check((await db.SellerSettlements.SingleAsync()).Status == "OnHold", "approved return must hold funds");
        var disputeService = new DisputeService(db, finance, service, new ConfigurationBuilder().Build(), TimeProvider.System);
        await disputeService.MaintainAsync();
        Check(await db.Disputes.CountAsync() == 0, "ordinary return hold must not become a legacy dispute");
        var overlapRejected = false;
        try { await disputeService.OpenAsync(1, 1, new("Yêu cầu khác", ["https://example.test/evidence"])); }
        catch (InvalidOperationException) { overlapRejected = true; }
        Check(overlapRejected, "buyer dispute cannot share an ordinary return hold");
        var parcel = await db.ShippingInfos.SingleAsync(x => x.Direction == "Return");
        parcel.Status = "Delivered";
        parcel.DeliveredAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await service.MarkReceivedAsync(request.Id);
        Check(request.Status == "Refunded", "receipt must automatically refund");
        await service.MarkReceivedAsync(request.Id);
        Check(await db.Refunds.CountAsync(x => x.Status == "Succeeded") == 1, "repeated receipt refunds once");
        Check((await db.SellerAccounts.SingleAsync()).OnHoldBalance == 0, "successful refund consumes hold");
        Console.WriteLine("Ordinary return hold and automatic receipt checks passed");
        await TimeoutAndIssueAsync();
        await StaleReceiptAsync();
        await ConcurrentReleaseAsync();
    }

    private static async Task TimeoutAndIssueAsync()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var clock = new Clock();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Returns:ReceiptSeconds"] = "45" }).Build();
        db.Users.AddRange(new User { Id = 1, Role = "buyer" }, new User { Id = 2, Role = "seller" }, new User { Id = 3, Role = "admin" });
        for (var id = 1; id <= 2; id++)
        {
            db.OrderTables.Add(new OrderTable { Id = id, BuyerId = 1, SellerId = 2, Status = "Delivered", TotalPrice = 100m });
            db.Payments.Add(new Payment { Id = id, OrderId = id, Status = "Succeeded", Amount = 100m, Method = "Card" });
            db.ShippingInfos.Add(new ShippingInfo { OrderId = id, Direction = "Outbound", Status = "Delivered", DeliveredAt = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();
        var finance = new SellerFinanceService(db, config);
        var gateway = new Gateway();
        var returns = new ReturnService(db, gateway, new Carrier(), finance, config, clock);
        var disputes = new DisputeService(db, finance, returns, config, clock);
        var request = await returns.RequestReturnAsync(1, "Hàng lỗi");
        await returns.MaintainAsync();
        Check(request.Status == "Requested", "request alone never refunds");
        await returns.ApproveAsync(request.Id);
        var parcel = await db.ShippingInfos.SingleAsync(x => x.OrderId == 1 && x.Direction == "Return");
        parcel.Status = "Delivered"; parcel.DeliveredAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        clock.Advance(44);
        await returns.MaintainAsync();
        Check(request.Status == "ReturnShipping", "return timeout cannot run before delivery plus 45 seconds");
        clock.Advance(1);
        gateway.Failures = 1;
        await returns.MaintainAsync();
        Check(request.Status == "RefundFailed", "failed timeout refund stays retryable");
        Check((await db.SellerAccounts.SingleAsync()).OnHoldBalance > 0, "failed refund retains held funds");
        await returns.MaintainAsync();
        Check(request.Status == "Refunded", "worker retries failed ordinary refund");
        await returns.MaintainAsync();
        Check(await db.Refunds.CountAsync(x => x.OrderId == 1) == 1, "retry reuses refund record");

        var issueReturn = await returns.RequestReturnAsync(2, "Sai hàng");
        await returns.ApproveAsync(issueReturn.Id);
        var issueParcel = await db.ShippingInfos.SingleAsync(x => x.OrderId == 2 && x.Direction == "Return");
        issueParcel.Status = "Delivered"; issueParcel.DeliveredAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        var issue = await disputes.ReportReturnIssueAsync(issueReturn.Id, 2, new("Hàng trả bị tráo", ["https://example.test/evidence"]));
        Check(issue.Status == "Escalated" && issueReturn.Status == "Disputed", "seller issue escalates and blocks return");
        clock.Advance(46);
        await returns.MaintainAsync();
        Check(!await db.Refunds.AnyAsync(x => x.OrderId == 2), "disputed returned parcel must not auto-refund");
        await disputes.ResolveAsync(issue.Id, 3, new(true, "Hàng trả hợp lệ"));
        Check((await db.OrderTables.SingleAsync(x => x.Id == 2)).Status == "Closed", "admin decision executes refund");
        Check((await db.ReturnRequests.SingleAsync(x => x.Id == issueReturn.Id)).Status == "Refunded", "admin refund completes return too");
        Console.WriteLine("Ordinary return deadline, retry and seller issue checks passed");
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }

    private static async Task StaleReceiptAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var manual = new ApplicationDbContext(options);
        manual.AddRange(new User { Id = 1, Role = "buyer" }, new User { Id = 2, Role = "seller" },
            new OrderTable { Id = 1, BuyerId = 1, SellerId = 2, Status = "Delivered", TotalPrice = 100m },
            new Payment { Id = 1, OrderId = 1, Status = "Succeeded", Amount = 100m, Method = "Card" },
            new ShippingInfo { OrderId = 1, Direction = "Outbound", Status = "Delivered", DeliveredAt = DateTime.UtcNow });
        await manual.SaveChangesAsync();
        var gateway = new Gateway();
        var config = new ConfigurationBuilder().Build();
        var returns = new ReturnService(manual, gateway, new Carrier(), new SellerFinanceService(manual, config));
        var request = await returns.RequestReturnAsync(1, "Hàng lỗi");
        await returns.ApproveAsync(request.Id);
        var parcel = await manual.ShippingInfos.SingleAsync(x => x.Direction == "Return");
        parcel.Status = "Delivered"; parcel.DeliveredAt = DateTime.UtcNow.AddDays(-3);
        await manual.SaveChangesAsync();
        await using var worker = new ApplicationDbContext(options);
        await worker.ReturnRequests.SingleAsync(); // Worker read just before the seller's confirmation.
        await returns.MarkReceivedAsync(request.Id);
        await new ReturnService(worker, gateway, new Carrier(), new SellerFinanceService(worker, config)).MarkReceivedAsync(request.Id);
        await using var fresh = new ApplicationDbContext(options);
        Check((await fresh.ReturnRequests.SingleAsync()).Status == "Refunded", "stale receipt must not overwrite completed refund");
        Check(await fresh.Refunds.CountAsync(x => x.Status == "Succeeded") == 1, "stale receipt cannot refund twice");
        Console.WriteLine("Concurrent manual/worker receipt regression passed");
    }

    private static async Task ConcurrentReleaseAsync()
    {
        var database = Guid.NewGuid().ToString();
        var root = new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(database, root).Options;
        await using var seller = new ApplicationDbContext(options);
        seller.AddRange(new User { Id = 1, Role = "buyer" }, new User { Id = 2, Role = "seller" },
            new OrderTable { Id = 1, BuyerId = 1, SellerId = 2, Status = "Delivered", TotalPrice = 100m },
            new Payment { Id = 1, OrderId = 1, Status = "Succeeded", Amount = 100m, Method = "Card" },
            new ShippingInfo { OrderId = 1, Direction = "Outbound", Status = "Delivered", DeliveredAt = DateTime.UtcNow });
        await seller.SaveChangesAsync();
        var config = new ConfigurationBuilder().Build();
        var finance = new SellerFinanceService(seller, config);
        var settlement = await finance.RecordSuccessfulPaymentAsync(1, 1);
        settlement.ReleaseAt = DateTime.UtcNow.AddSeconds(-1);
        await seller.SaveChangesAsync();
        var returns = new ReturnService(seller, new Gateway(), new Carrier(), finance);
        var trigger = new ReleaseRace(() => {
            var request = returns.RequestReturnAsync(1, "Hàng lỗi").GetAwaiter().GetResult();
            returns.ApproveAsync(request.Id).GetAwaiter().GetResult();
        });
        var workerOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(database, root).AddInterceptors(trigger).Options;
        await using var worker = new ApplicationDbContext(workerOptions);
        await new SellerFinanceService(worker, config).ReleaseDueFundsAsync();
        await using var fresh = new ApplicationDbContext(options);
        var account = await fresh.SellerAccounts.SingleAsync();
        Check(trigger.Triggered, "release race fixture must trigger");
        Check(account.AvailableBalance == 0 && account.ProcessingBalance == 0, "release cannot make a concurrently held return withdrawable");
        Check((await fresh.SellerSettlements.SingleAsync()).Status == "OnHold", "release must not overwrite a concurrently placed hold");
        Console.WriteLine("Concurrent release/return-hold regression passed");
    }

    private sealed class ReleaseRace(Action approve) : IMaterializationInterceptor
    {
        public bool Triggered { get; private set; }
        public object InitializedInstance(MaterializationInterceptionData data, object entity)
        {
            if (entity is SellerSettlement && !Triggered) { Triggered = true; approve(); }
            return entity;
        }
    }

    internal static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    internal sealed class Carrier : ICarrierGateway
    {
        public Task<string> CreateLabelAsync(int orderId, string direction, string key, CancellationToken ct) =>
            Task.FromResult($"TRACK-{orderId}-{direction}");
    }

    internal sealed class Gateway : IRefundGateway
    {
        public int Failures { get; set; }
        public Task<string> RefundAsync(Payment payment, decimal amount, string key, CancellationToken ct)
        {
            if (Failures-- > 0) throw new HttpRequestException("Temporary refund error");
            return Task.FromResult(key);
        }
    }
}
