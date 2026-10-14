using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Disputes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class DisputeDecisionChecks
{
    public static async Task RunAsync()
    {
        await PricedReturnAsync("WrongItem", "Buyer", "FullRefund", null, 85m, 4.39m);
        await PricedReturnAsync("ChangedMind", "Seller", "FullRefund", null, 95m, -5m);
        await PricedReturnAsync("WrongItem", "Buyer", "PartialRefund", 40m, 40m, 48.24m);
        await PricedReturnAsync("ChangedMind", "Seller", "PartialRefund", 40m, 40m, 48.32m, 1);
        await RetryAndGuardsAsync();
        await NoReturnAndDebtAsync();
        await ZeroAndBlockedAsync();
        await ShippingAdjustmentAsync();
        Console.WriteLine("Admin decisions: responsibility, partial funding, fee credits, immutable evidence decisions, stale previews, retries, zero refunds, Instant/debt and hold release passed");
    }
    private static async Task PricedReturnAsync(string originalReason, string responsibility, string outcome, decimal? amount,
        decimal cash, decimal signedBalance, int level = 3)
    {
        await using var f = await Fixture.CreateAsync(level);
        var dispute = await f.ReturnIssueAsync(originalReason);
        var request = await f.Db.ReturnRequests.SingleAsync();
        var originalRefund = request.BuyerRefundAmount;
        var q = await f.Service.PreviewDecisionAsync(dispute.Id, 3, new(outcome, responsibility, amount));
        Equal(cash, q.BuyerRefund, "decision cash preview");
        var input = await f.InputAsync(dispute.Id, q, amount);
        var result = await f.Service.ResolveDecisionAsync(dispute.Id, 3, input);
        Equal("Succeeded", result.RefundStatus, "decision executed");
        Equal(cash, f.Gateway.Amounts.Single(), "gateway cash matches decision");
        Equal(originalRefund, request.BuyerRefundAmount, "original buyer quote remains unchanged");
        Equal(outcome == "PartialRefund" ? "AdminPartialRefund" : "AdminFullRefund", dispute.Outcome, "decision outcome");
        Equal("Closed", dispute.Status, "case closes only after refund succeeds");
        Equal(0m, f.Account.OnHoldBalance, "remainder hold cleared");
        Equal(Math.Max(0, signedBalance), level == 3 ? f.Account.AvailableBalance : f.Account.ProcessingBalance, "wallet or processing matches accounting");
        Equal(Math.Max(0, -signedBalance), f.Account.NegativeBalance, "debt matches accounting");
        Equal(5m, f.Settlement.ReturnShippingCost, "carrier cost not charged again");
        Equal(responsibility == "Buyer" ? 5m : 0m, f.Settlement.ReturnShippingRecovery, "payer reclassification recovery");
        var replay = await f.Service.ResolveDecisionAsync(dispute.Id, 3, input);
        Equal(result.Id, replay.Id, "same decision retry"); Equal(1, f.Gateway.Amounts.Count, "retry no extra provider refund");
        await f.Service.MaintainAsync(); await f.Finance.BackfillAsync();
        Equal(1, await f.Db.FinancialTransactions.CountAsync(x => x.Type == "RefundApplied"), "worker no extra ledger");
        if (level == 1)
        {
            Equal(0m, f.Account.AvailableBalance, "partial remainder observes original delivery hold");
            f.Clock.Advance(30); await f.Finance.ReleaseDueFundsAsync();
            Equal(signedBalance, f.Account.AvailableBalance, "remainder available after original deadline");
        }
    }
    private static async Task RetryAndGuardsAsync()
    {
        await using var f = await Fixture.CreateAsync(3);
        var d = await f.ReturnIssueAsync("WrongItem");
        await RejectAsync(() => f.Service.PreviewDecisionAsync(d.Id, 2, new("FullRefund", "Seller", null)));
        await RejectAsync(() => f.Service.PreviewDecisionAsync(d.Id, 3, new("PartialRefund", "Buyer", 86m)));
        await RejectAsync(() => f.Service.PreviewDecisionAsync(d.Id, 3, new("PartialRefund", "Seller", 40.001m)));
        await RejectAsync(() => f.Service.PreviewDecisionAsync(d.Id, 3, new("SellerWins", "Seller", null)));
        var q = await f.Service.PreviewDecisionAsync(d.Id, 3, new("PartialRefund", "Buyer", 40m));
        var input = await f.InputAsync(d.Id, q, 40m);
        await RejectAsync(() => f.Service.ResolveDecisionAsync(d.Id, 3, input with { EvidenceEntryIds = [999] }));
        await f.Service.AddEvidenceAsync(d.Id, 1, "buyer", new("New evidence", ["https://example.test/new"]));
        await RejectAsync(() => f.Service.ResolveDecisionAsync(d.Id, 3, input));
        Equal(0, await f.Db.DisputeDecisions.CountAsync(), "stale quote creates no decision");
        q = await f.Service.PreviewDecisionAsync(d.Id, 3, new("PartialRefund", "Buyer", 40m));
        input = await f.InputAsync(d.Id, q, 40m);
        f.Gateway.Failures = 1;
        var saved = await f.Service.ResolveDecisionAsync(d.Id, 3, input);
        Equal("Failed", saved.RefundStatus, "failed gateway stays pending");
        Equal("ExecutingAgreement", d.Status, "decision persists before retry");
        Equal(87.10m, f.Account.OnHoldBalance, "failed refund keeps hold after postage");
        await RejectAsync(() => f.Service.ResolveDecisionAsync(d.Id, 3, input with { RefundAmount = 41m }));
        var decision = await f.Db.DisputeDecisions.SingleAsync(); decision.Reason = "Tampered";
        await RejectAsync(() => f.Db.SaveChangesAsync()); f.Db.Entry(decision).Reload();
        await f.Service.MaintainAsync(); await f.Service.MaintainAsync();
        Check(f.Gateway.Amounts.Count == 2 && f.Gateway.Amounts.All(x => x == 40m), "gateway retries immutable decision amount");
        Equal(48.24m, f.Account.AvailableBalance, "retry reconciliation");
    }
    private static async Task NoReturnAndDebtAsync()
    {
        await using var f = await Fixture.CreateAsync(3);
        var d = await f.EscalateAsync();
        var q = await f.Service.PreviewDecisionAsync(d.Id, 3, new("PartialRefund", "Seller", 40m));
        Equal(0m, q.ReturnShippingCost, "no parcel means no guessed return fee");
        await f.Service.ResolveDecisionAsync(d.Id, 3, await f.InputAsync(d.Id, q, 40m));
        Equal(53.32m, f.Account.AvailableBalance, "partial refund with buyer keeping goods");
        await using var debt = await Fixture.CreateAsync(3);
        await debt.Finance.RequestPayoutAsync(2, 92.10m, "payout-before-dispute", false);
        var debtCase = await debt.ReturnIssueAsync("ChangedMind");
        var debtQ = await debt.Service.PreviewDecisionAsync(debtCase.Id, 3, new("FullRefund", "Seller", null));
        await debt.Service.ResolveDecisionAsync(debtCase.Id, 3, await debt.InputAsync(debtCase.Id, debtQ, null));
        Equal(97.10m, debt.Account.NegativeBalance, "Instant payout before refund incurs debt including postage");
        await using var seller = await Fixture.CreateAsync(1);
        var sellerCase = await seller.EscalateAsync();
        var sellerQ = await seller.Service.PreviewDecisionAsync(sellerCase.Id, 3, new("SellerWins", "Buyer", null));
        await seller.Service.ResolveDecisionAsync(sellerCase.Id, 3, await seller.InputAsync(sellerCase.Id, sellerQ, null));
        Equal(0, seller.Gateway.Amounts.Count, "seller win has no provider refund");
        Equal(92.10m, seller.Account.ProcessingBalance, "seller win obeys original schedule");
    }
    private static async Task ZeroAndBlockedAsync()
    {
        await using var f = await Fixture.CreateAsync(3, 10m, 5m, 10m);
        var d = await f.ReturnIssueAsync("WrongItem");
        var q = await f.Service.PreviewDecisionAsync(d.Id, 3, new("FullRefund", "Buyer", null));
        Equal(0m, q.BuyerRefund, "postage equals eligible goods cash");
        await f.Service.ResolveDecisionAsync(d.Id, 3, await f.InputAsync(d.Id, q, null));
        Equal(0, f.Gateway.Amounts.Count, "zero refund internal");
        Equal(4.33m, f.Account.AvailableBalance, "zero refund money reconciles");
        await using var blocked = await Fixture.CreateAsync(3, 6m, 5m, 0m);
        var blockedCase = await blocked.ReturnIssueAsync("WrongItem");
        await RejectAsync(() => blocked.Service.PreviewDecisionAsync(blockedCase.Id, 3, new("FullRefund", "Buyer", null)));
        Equal(0, await blocked.Db.DisputeDecisions.CountAsync(), "excessive postage cannot create negative refund");
        await using var promotion = await Fixture.CreateAsync(3, 0m, 0m, 10m);
        var promotionCase = await promotion.ReturnIssueAsync("WrongItem");
        var promotionQ = await promotion.Service.PreviewDecisionAsync(promotionCase.Id, 3, new("FullRefund", "Seller", null));
        await promotion.Service.ResolveDecisionAsync(promotionCase.Id, 3, await promotion.InputAsync(promotionCase.Id, promotionQ, null));
        Equal(0, promotion.Gateway.Amounts.Count, "coupon-funded zero payment has no provider refund");
        Equal(5m, promotion.Account.NegativeBalance, "coupon-funded full decision reverses funding and leaves postage expense");
    }
    private static async Task ShippingAdjustmentAsync()
    {
        await using var f = await Fixture.CreateAsync(3);
        var shippingRefund = new Refund { OrderId = 1, PaymentId = 1, Amount = 3m, Reason = "shipping-adjustment",
            Status = "Succeeded", IdempotencyKey = "shipping-adjustment" };
        f.Db.Refunds.Add(shippingRefund); await f.Db.SaveChangesAsync(); await f.Finance.ApplyRefundAsync(1, shippingRefund.Id);
        var d = await f.ReturnIssueAsync("WrongItem");
        var q = await f.Service.PreviewDecisionAsync(d.Id, 3, new("PartialRefund", "Buyer", 40m));
        Equal(2m, q.OutboundShippingRetained, "already refunded shipping not retained twice");
        Equal(6.51m, q.FeeCredit, "fee credit accounts for prior gross reversal");
        await f.Service.ResolveDecisionAsync(d.Id, 3, await f.InputAsync(d.Id, q, 40m));
        Equal(45.61m, f.Account.AvailableBalance, "shipping adjustment and partial decision reconcile");
        Equal(43m, await f.Db.Refunds.Where(x => x.Status == "Succeeded").SumAsync(x => x.Amount), "buyer aggregate cash within capture");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual, string message) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, got {actual}");
    private static async Task RejectAsync(Func<Task> action)
    {
        try { await action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or UnauthorizedAccessException) { return; }
        throw new Exception("Expected decision rejection");
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public DateTime Utc => now.UtcDateTime;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }
    private sealed class Gateway : IRefundGateway
    {
        public List<decimal> Amounts = [];
        public int Failures;
        public Task<string> RefundAsync(Payment payment, decimal amount, string key, CancellationToken ct)
        { Amounts.Add(amount); if (Failures-- > 0) throw new HttpRequestException("Temporary refund failure"); return Task.FromResult(key); }
    }
    private sealed class Carrier : ICarrierGateway
    {
        public Task<string> CreateLabelAsync(int orderId, string direction, string key, CancellationToken ct) => Task.FromResult(key);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db = null!;
        public Clock Clock = new(); public Gateway Gateway = new();
        public SellerFinanceService Finance = null!; public ReturnService Returns = null!;
        public DisputeService Service = null!; public SellerAccount Account = null!; public SellerSettlement Settlement = null!;
        public static async Task<Fixture> CreateAsync(int level, decimal paid = 95m, decimal ship = 5m, decimal subsidy = 10m)
        {
            var f = new Fixture();
            f.Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Finance:Levels:1:HoldSeconds"] = "30", ["Returns:ReceiptSeconds"] = "45", ["Disputes:SellerResponseSeconds"] = "1" }).Build();
            f.Db.AddRange(new User { Id = 1, Role = "buyer", Email = "buyer@test" }, new User { Id = 2, Role = "seller", Email = "seller@test" }, new User { Id = 3, Role = "admin", Email = "admin@test" },
                new SellerAccount { Id = 1, SellerId = 2, Level = level, MonthlySalesLimit = 1000000m, HoldDays = 21, SalesMonth = new DateTime(f.Clock.Utc.Year, f.Clock.Utc.Month, 1) },
                new OrderTable { Id = 1, BuyerId = 1, SellerId = 2, Status = "Delivered", TotalPrice = paid, ShippingFee = ship,
                    PricingSchemaVersion = 1, PlatformSubsidy = subsidy, SellerGrossSnapshot = paid + subsidy, Subtotal = paid - ship + subsidy,
                    TotalWeightKg = 1m, PickupStateSnapshot = "Hanoi", PickupCountrySnapshot = "Vietnam", DeliveryStateSnapshot = "HCM", DeliveryCountrySnapshot = "Vietnam",
                    PickupAddressSnapshot = "Seller, Road, City, Hanoi, Vietnam", AddressSnapshot = "Buyer, Road, City, HCM, Vietnam" },
                new Payment { Id = 1, OrderId = 1, Status = "Succeeded", Amount = paid, Method = paid == 0 ? "Promotion" : "Card", PaidAt = f.Clock.Utc },
                new ShippingInfo { OrderId = 1, Direction = "Outbound", Status = "Delivered", DeliveredAt = f.Clock.Utc });
            await f.Db.SaveChangesAsync();
            f.Finance = new(f.Db, config, f.Clock); f.Settlement = await f.Finance.RecordSuccessfulPaymentAsync(1, 1);
            f.Account = await f.Db.SellerAccounts.SingleAsync(); f.Returns = new(f.Db, f.Gateway, new Carrier(), f.Finance, config, f.Clock);
            f.Service = new(f.Db, f.Finance, f.Returns, config, f.Clock);
            return f;
        }
        public async Task<Dispute> ReturnIssueAsync(string reason)
        {
            var q = await Returns.QuoteAsync(1, reason);
            var r = await Returns.RequestPricedReturnAsync(1, new(reason, "Original buyer request", "original-return", q.PricingFingerprint));
            await Returns.ApproveAsync(r.Id);
            var parcel = await Db.ShippingInfos.SingleAsync(x => x.Direction == "Return"); parcel.Status = "Delivered"; parcel.DeliveredAt = Clock.Utc;
            await Db.SaveChangesAsync();
            return await Service.ReportReturnIssueAsync(r.Id, 2, new("Seller disputes condition", ["https://example.test/evidence"]));
        }
        public async Task<Dispute> EscalateAsync()
        {
            var d = await Service.OpenAsync(1, 1, new("Buyer evidence", ["https://example.test/evidence"]));
            Clock.Advance(1); await Service.MaintainAsync(); return d;
        }
        public async Task<AdminDecisionInput> InputAsync(int id, AdminDecisionQuote quote, decimal? amount)
        {
            var evidence = await Db.DisputeEntries.Where(x => x.DisputeId == id && (x.Kind == "Evidence" || x.Kind == "ReturnIssue")).Select(x => x.Id).ToArrayAsync();
            return new(quote.Outcome, quote.Responsibility, amount, "Reviewed original evidence and tracking", evidence, Guid.NewGuid().ToString(), quote.PricingFingerprint);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
