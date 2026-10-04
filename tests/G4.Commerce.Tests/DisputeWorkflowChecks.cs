using G4.Domain.Entities;
using G4.Infrastructure.Disputes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class DisputeWorkflowChecks
{
    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Disputes:SellerResponseSeconds"] = "45", ["Disputes:BuyerResponseSeconds"] = "45",
            ["Disputes:ReturnReceiptSeconds"] = "45", ["Disputes:ExecutionTimeoutSeconds"] = "1"
        }).Build();
        var clock = new DisputeClock();
        var finance = new SellerFinanceService(db, config);
        var gateway = new FlakyRefundGateway();
        var carrier = new DisputeTestCarrier();
        var returns = new ReturnService(db, gateway, carrier, finance);
        var service = new DisputeService(db, finance, returns, config, clock);
        db.Users.AddRange(new User { Id = 1, Email = "buyer@test", Role = "buyer" },
            new User { Id = 2, Email = "seller@test", Role = "seller" },
            new User { Id = 3, Email = "admin@test", Role = "admin" });
        for (var id = 601; id <= 610; id++)
        {
            db.OrderTables.Add(new OrderTable { Id = id, BuyerId = 1, SellerId = 2, Status = "Delivered",
                OrderDate = clock.GetUtcNow().UtcDateTime, TotalPrice = 100m });
            db.Payments.Add(new Payment { Id = id, OrderId = id, Method = "Card", Status = "Succeeded", Amount = 100m });
            db.ShippingInfos.Add(new ShippingInfo { OrderId = id, Direction = "Outbound", Status = "Delivered",
                DeliveredAt = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();
        for (var id = 601; id <= 610; id++) await finance.RecordSuccessfulPaymentAsync(id, id);
        var links = new[] { "https://example.test/evidence" };

        await Reject<ArgumentException>(() => service.OpenAsync(601, 1, new("Mô tả", [])));
        await Reject<ArgumentException>(() => service.OpenAsync(601, 1, new(" ", links)));
        await Reject<ArgumentException>(() => service.OpenAsync(601, 1, new("Mô tả", ["javascript:alert(1)"])));
        await Reject<UnauthorizedAccessException>(() => service.OpenAsync(601, 9, new("Mô tả", links)));
        Check(0, await db.Disputes.CountAsync(), "invalid request creates no case");
        var first = await service.OpenAsync(601, 1, new("Hàng bị lỗi", links));
        Check("AwaitingSeller", first.Status, "opening awaits seller");
        Check(clock.GetUtcNow().UtcDateTime.AddSeconds(45), first.SellerResponseDueAt, "seller deadline");
        Check(first.Id, (await service.OpenAsync(601, 1, new("Hàng bị lỗi", links))).Id, "duplicate opening");
        Check(1, await db.DisputeEntries.CountAsync(x => x.DisputeId == first.Id), "opening evidence saved once");
        await Reject<InvalidOperationException>(() => service.ResolveAsync(first.Id, 3, new(true, "Buyer wins")));
        await Reject<UnauthorizedAccessException>(() => service.GetDetailAsync(first.Id, 9, "buyer"));
        await Reject<UnauthorizedAccessException>(() => service.GetDetailAsync(first.Id, 3, "admin"));
        var namedDetail = await service.GetDetailAsync(first.Id, 1, "buyer");
        Check("buyer@test", namedDetail.BuyerName, "buyer display name falls back to email");
        Check("seller@test", namedDetail.SellerName, "seller display name falls back to email");

        await service.ProposeAsync(first.Id, 2, new("KeepOrder", "Hàng đúng mô tả", links));
        var buyerDeadline = first.BuyerResponseDueAt;
        await service.AddEvidenceAsync(first.Id, 1, "buyer", new("Ảnh bổ sung", links));
        Check(buyerDeadline, first.BuyerResponseDueAt, "evidence does not extend deadline");
        await Reject<InvalidOperationException>(() => service.ProposeAsync(first.Id, 2, new("Refund", "Đổi phương án", links)));
        clock.Advance(45);
        await service.MaintainAsync();
        Check("Closed", first.Status, "buyer silence closes");
        Check("BuyerSilent", first.Outcome, "silence is not consent");
        Check("Processing", (await db.SellerSettlements.SingleAsync(x => x.OrderId == 601)).Status, "normal hold still applies");
        await Reject<InvalidOperationException>(() => service.AddEvidenceAsync(first.Id, 1, "buyer", new("Muộn", links)));
        var reopened = await service.OpenAsync(601, 1, new("Vấn đề vẫn còn", links));
        if (reopened.Id == first.Id) throw new Exception("closed case reused");
        Check("OnHold", (await db.SellerSettlements.SingleAsync(x => x.OrderId == 601)).Status, "reopened case reserves funds");
        clock.Advance(45);
        await service.MaintainAsync();
        Check("Escalated", reopened.Status, "seller silence escalates");
        var historyCount = await db.DisputeEntries.CountAsync();
        await service.MaintainAsync();
        Check(historyCount, await db.DisputeEntries.CountAsync(), "maintenance is idempotent");
        await service.ResolveAsync(reopened.Id, 3, new(false, "Bằng chứng người bán phù hợp"));
        Check("SellerWins", reopened.Outcome, "admin seller outcome");

        var refundCase = await service.OpenAsync(602, 1, new("Sản phẩm hỏng", links));
        await service.ProposeAsync(refundCase.Id, 2, new("Refund", "Đồng ý hoàn tiền", links));
        gateway.FailuresRemaining = 1;
        await service.RespondAsync(refundCase.Id, 1, new(true, "Đồng ý"));
        Check("ExecutingAgreement", refundCase.Status, "failed refund leaves case open");
        Check("RefundPending", (await db.SellerSettlements.SingleAsync(x => x.OrderId == 602)).Status, "failed refund keeps reservation");
        await service.MaintainAsync();
        Check("Closed", refundCase.Status, "successful refund closes agreement");
        Check("AgreedRefund", refundCase.Outcome, "agreed refund outcome");
        await service.MaintainAsync();
        Check(1, await db.Refunds.CountAsync(x => x.OrderId == 602 && x.Status == "Succeeded"), "refund once");

        var adminCase = await service.OpenAsync(603, 1, new("Sai sản phẩm", links));
        await service.ProposeAsync(adminCase.Id, 2, new("KeepOrder", "Không đồng ý", links));
        await service.RespondAsync(adminCase.Id, 1, new(false, "Chưa giải quyết được"));
        Check("Escalated", adminCase.Status, "disagreement escalates");
        await Reject<ArgumentException>(() => service.ResolveAsync(adminCase.Id, 3, new(true, " ")));
        await service.ResolveAsync(adminCase.Id, 3, new(true, "Ảnh chứng minh hàng sai"));
        Check("BuyerWins", adminCase.Outcome, "admin refund outcome");
        var returnCase = await service.OpenAsync(604, 1, new("Cần trả hàng", links));
        await service.ProposeAsync(returnCase.Id, 2, new("ReturnRefund", "Trả hàng để hoàn tiền", links));
        var outbound = await db.ShippingInfos.SingleAsync(x => x.OrderId == 604 && x.Direction == "Outbound");
        outbound.DeliveredAt = DateTime.UtcNow.AddDays(-8);
        await db.SaveChangesAsync();
        Check(0, await new CheckoutService(db).CloseDeliveredOutsideReturnWindowAsync(), "active case keeps order eligible");
        carrier.Timeout = true;
        await service.RespondAsync(returnCase.Id, 1, new(true, "Đồng ý trả"));
        Check("ExecutingAgreement", returnCase.Status, "return not prematurely closed");
        var request = await db.ReturnRequests.SingleAsync(x => x.OrderId == 604);
        var parcel = await db.ShippingInfos.SingleAsync(x => x.OrderId == 604 && x.Direction == "Return");
        Check("ShipmentCreationFailed", parcel.Status, "carrier timeout keeps accepted agreement for retry");
        Check("OnHold", (await db.SellerSettlements.SingleAsync(x => x.OrderId == 604)).Status, "carrier timeout retains funds");
        clock.Advance(45);
        await service.MaintainAsync();
        Check("ExecutingAgreement", returnCase.Status, "accepted agreement never becomes buyer silence");
        carrier.Timeout = false;
        await service.MaintainAsync();
        Check("LabelCreated", parcel.Status, "carrier timeout retries label successfully");
        parcel.Status = "Delivered";
        parcel.DeliveredAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        await service.MaintainAsync();
        Check("ExecutingAgreement", returnCase.Status, "receipt waits for configured deadline");
        clock.Advance(45);
        await service.MaintainAsync();
        Check("Closed", returnCase.Status, "returned and refunded agreement closes");
        Check("AgreedReturn", returnCase.Outcome, "return agreement outcome");

        var lateCase = await service.OpenAsync(605, 1, new("Kiểm tra hết hạn", links));
        clock.Advance(45);
        await Reject<InvalidOperationException>(() => service.ProposeAsync(lateCase.Id, 2, new("Refund", "Muộn", links)));
        await service.MaintainAsync();
        Check("Escalated", lateCase.Status, "late reply cannot bypass deadline");
        var page = await service.GetPageAsync(1, "buyer", 1, 2, "all");
        Check(2, page.Items.Count, "server pagination");
        Check(0, (await service.GetPageAsync(9, "seller", 1, 10, "all")).TotalCount, "ownership filter");
        Check(1, (await service.GetPageAsync(3, "admin", 1, 10, "open")).TotalCount, "admin sees escalated open cases only");
        var evidence = await db.DisputeEntries.FirstAsync(x => x.Kind == "Evidence");
        evidence.Description = "Sửa trái phép";
        await Reject<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(evidence).State = EntityState.Unchanged;
        db.DisputeEntries.Remove(evidence);
        await Reject<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(evidence).State = EntityState.Unchanged;
        // A large admin queue must not prevent a later negotiation deadline from being processed.
        for (var i = 0; i < 100; i++)
            db.Disputes.Add(new Dispute { WorkflowEnabled = true, IsOpen = true, Status = "Escalated",
                UpdatedAt = clock.GetUtcNow().UtcDateTime.AddDays(-1) });
        await db.SaveChangesAsync();
        var longDescription = new string('a', 600);
        var queuedCase = await service.OpenAsync(606, 1, new(longDescription, links));
        Check(longDescription, (await db.DisputeEntries.SingleAsync(x => x.DisputeId == queuedCase.Id)).Description, "full evidence description retained");
        Check(500, (await db.FinancialTransactions.SingleAsync(x => x.OrderId == 606 && x.Type == "FundHold")).Description!.Length, "finance summary fits SQL column");
        clock.Advance(45);
        await service.MaintainAsync();
        Check("Escalated", queuedCase.Status, "admin queue cannot starve overdue negotiation");

        var faultCase = await service.OpenAsync(607, 1, new("Kiểm tra cách ly lỗi thực hiện", links));
        await service.ProposeAsync(faultCase.Id, 2, new("ReturnRefund", "Đồng ý trả", links));
        carrier.UnexpectedFailure = true;
        await service.RespondAsync(faultCase.Id, 1, new(true, "Đồng ý"));
        Check("ExecutingAgreement", (await db.Disputes.SingleAsync(x => x.Id == faultCase.Id)).Status, "execution error cannot undo acceptance");
        var followingCase = await service.OpenAsync(608, 1, new("Kiểm tra yêu cầu tiếp theo", links));
        await service.ProposeAsync(followingCase.Id, 2, new("Refund", "Đồng ý hoàn", links));
        gateway.FailuresRemaining = 1;
        await service.RespondAsync(followingCase.Id, 1, new(true, "Đồng ý"));
        clock.Advance(5);
        await service.MaintainAsync();
        Check("Closed", (await db.Disputes.SingleAsync(x => x.Id == followingCase.Id)).Status, "failing case does not block following agreement");
        Check("OnHold", (await db.SellerSettlements.SingleAsync(x => x.OrderId == 607)).Status, "unexpected carrier failure keeps hold");
        carrier.UnexpectedFailure = false;
        carrier.Stall = true;
        var slowCase = await service.OpenAsync(609, 1, new("Kiểm tra cổng vận chuyển treo", links));
        await service.ProposeAsync(slowCase.Id, 2, new("ReturnRefund", "Đồng ý trả", links));
        await service.RespondAsync(slowCase.Id, 1, new(true, "Đồng ý"));
        var expiringCase = await service.OpenAsync(610, 1, new("Kiểm tra deadline khi cổng ngoài treo", links));
        clock.Advance(45);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await service.MaintainAsync();
        if (stopwatch.Elapsed > TimeSpan.FromSeconds(3)) throw new Exception("Stalled gateway exceeded maintenance execution budget");
        Check("Escalated", (await db.Disputes.SingleAsync(x => x.Id == expiringCase.Id)).Status, "deadlines processed before stalled gateway retry");
        Check("ExecutingAgreement", (await db.Disputes.SingleAsync(x => x.Id == slowCase.Id)).Status, "execution cancellation preserves acceptance");
        carrier.Stall = false;
        await service.MaintainAsync();
        Check("ReturnShipping", (await db.ReturnRequests.SingleAsync(x => x.OrderId == 609)).Status, "interrupted label creation retries");
        Console.WriteLine("Dispute negotiation, deadlines, evidence and settlement checks passed");
    }

    private static void Check<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{message}: expected {expected}, got {actual}");
    }

    private static async Task Reject<T>(Func<Task> work) where T : Exception
    {
        try { await work(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
}

internal sealed class DisputeTestCarrier : ICarrierGateway
{
    public bool Timeout { get; set; }
    public bool UnexpectedFailure { get; set; }
    public bool Stall { get; set; }
    public async Task<string> CreateLabelAsync(int orderId, string direction, string key, CancellationToken ct)
    {
        if (Timeout) throw new TaskCanceledException("Simulated HTTP timeout");
        if (UnexpectedFailure) throw new InvalidOperationException("Simulated carrier integration failure");
        if (Stall) await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
        return $"TRACK-{orderId}-{direction}";
    }
}

internal sealed class DisputeClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(int seconds) => now = now.AddSeconds(seconds);
}
