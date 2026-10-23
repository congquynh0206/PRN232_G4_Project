using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Disputes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class ReturnPolicyChecks
{
    public static async Task RunAsync()
    {
        await StandardAsync("WrongItem", 95m, 5m, 0m);
        await StandardAsync("ChangedMind", 85m, 0m, 4.39m);
        await StandardAsync("ChangedMind", 85m, 0m, 4.39m, level: 3);
        await ShippingAdjustmentAsync();
        await FreeAndZeroAsync();
        await GuardsAsync();
        await RetryAsync();
        await AdminDecisionAsync();
        await NewAgreementAsync();
        Console.WriteLine(
            "Return policy: seller/buyer postage, preview snapshots, subsidies, fee credits, Instant, zero refunds, retries and remainder release passed"
        );
    }

    private static async Task StandardAsync(
        string reason,
        decimal refundAmount,
        decimal debt,
        decimal remaining,
        int level = 1
    )
    {
        await using var f = await Fixture.CreateAsync(level);
        var q = await f.Returns.QuoteAsync(1, reason);
        Equal(refundAmount, q.BuyerRefund, "quoted refund");
        Equal(5m, q.ReturnShippingFee, "quoted postage");
        Equal(90m, q.GoodsPaid, "buyer goods after platform coupon");
        Equal(10m, q.PlatformSubsidyReversal, "coupon is separate from buyer cash");
        var input = new ReturnRequestInput(
            reason,
            "<b>buyer description</b>",
            "request",
            q.PricingFingerprint
        );
        var request = await f.Returns.RequestPricedReturnAsync(1, input);
        Equal(
            request.Id,
            (await f.Returns.RequestPricedReturnAsync(1, input)).Id,
            "request retry idempotency"
        );
        await f.Returns.ApproveAsync(request.Id);
        await f.Returns.RetryShipmentAsync(request.Id);
        Equal(
            1,
            await f.Db.FinancialTransactions.CountAsync(x => x.Type == "ReturnShippingCost"),
            "postage is charged once"
        );
        Equal(5m, f.Settlement.ReturnShippingCost, "postage cost snapshot");
        await f.DeliverReturnAsync();
        await f.Returns.MarkReceivedAsync(request.Id);
        Equal("Refunded", request.Status, "return completed");
        Equal(
            refundAmount,
            f.Gateway.Amounts.Single(),
            "gateway receives exactly quoted buyer refund"
        );
        Equal(0m, f.Account.OnHoldBalance, "remaining hold is released");
        Equal(debt, f.Account.NegativeBalance, "only seller postage creates debt");
        Equal(
            remaining,
            level == 3 ? f.Account.AvailableBalance : f.Account.ProcessingBalance,
            "seller keeps outbound shipping after its fee"
        );
        Equal(
            reason == "WrongItem" ? 12.90m : 12.29m,
            f.Settlement.FeeCreditAmount,
            "fee credit on seller gross returned"
        );
        Equal(
            -10m,
            (
                await f.Db.FinancialTransactions.SingleAsync(x =>
                    x.Type == "PlatformSubsidyReversal"
                )
            ).Amount,
            "full goods coupon funding reversed"
        );
        await f.Returns.MarkReceivedAsync(request.Id);
        Equal(1, f.Gateway.Amounts.Count, "receipt retry never refunds twice");
        if (level != 3 && remaining > 0)
        {
            f.Clock.Advance(30);
            Equal(
                1,
                await f.Finance.ReleaseDueFundsAsync(),
                "remaining proceeds obey original hold"
            );
            Equal(remaining, f.Account.AvailableBalance, "remaining proceeds become available");
        }
        Equal(0, await f.Db.SellerPayouts.CountAsync(), "return flow never creates bank payout");
    }

    private static async Task ShippingAdjustmentAsync()
    {
        await using var f = await Fixture.CreateAsync(3);
        var order = await f.Db.OrderTables.SingleAsync();
        order.DeliveryStateSnapshot = "Hanoi";
        order.AddressSnapshot = "Buyer, Road, City, Hanoi, Vietnam";
        var adjustment = new Refund
        {
            OrderId = 1,
            PaymentId = 1,
            Amount = 3m,
            Reason = "shipping-adjustment",
            Status = "Succeeded",
            IdempotencyKey = "shipping-adjust",
        };
        f.Db.Refunds.Add(adjustment);
        await f.Db.SaveChangesAsync();
        await f.Finance.ApplyRefundAsync(1, adjustment.Id);
        var q = await f.Returns.QuoteAsync(1, "ChangedMind");
        Equal(2m, q.ReturnShippingFee, "return uses new route snapshot");
        Equal(
            2m,
            q.OutboundShippingRetained,
            "previous shipping refund removed from retained outbound cost"
        );
        Equal(88m, q.BuyerRefund, "no duplicate shipping refund");
        var r = await f.Returns.RequestPricedReturnAsync(
            1,
            new("ChangedMind", null, "adjusted-return", q.PricingFingerprint)
        );
        await f.Returns.ApproveAsync(r.Id);
        await f.DeliverReturnAsync();
        await f.Returns.MarkReceivedAsync(r.Id);
        Equal(
            91m,
            await f.Db.Refunds.Where(x => x.Status == "Succeeded").SumAsync(x => x.Amount),
            "total buyer cash refunded"
        );
        Equal(
            1.75m,
            f.Account.AvailableBalance,
            "retained shipping and fee credit reconcile after adjustment"
        );
        Equal(0m, f.Account.NegativeBalance, "platform coupon does not become debt");
    }

    private static async Task FreeAndZeroAsync()
    {
        foreach (var (paid, shipping, expectedWallet) in new[] { (10m, 5m, 4.25m), (5m, 0m, 0m) })
        {
            await using var f = await Fixture.CreateAsync(3, paid, shipping, 0m);
            var q = await f.Returns.QuoteAsync(1, "ChangedMind");
            Check(
                q.CanRequest && q.BuyerRefund == 0m,
                "postage equal to goods allows zero cash refund"
            );
            var r = await f.Returns.RequestPricedReturnAsync(
                1,
                new("ChangedMind", null, "zero", q.PricingFingerprint)
            );
            await f.Returns.ApproveAsync(r.Id);
            await f.DeliverReturnAsync();
            await f.Returns.MarkReceivedAsync(r.Id);
            Equal(0, f.Gateway.Amounts.Count, "zero refund never calls card or PayPal gateway");
            Equal(0m, f.Account.NegativeBalance, "fee credit recovers zero-refund postage debt");
            Equal(expectedWallet, f.Account.AvailableBalance, "zero refund reconciles wallet");
            Equal(0m, (await f.Db.Refunds.SingleAsync()).Amount, "actual zero refund is recorded");
        }
        await using var free = await Fixture.CreateAsync(3, 0m, 0m, 10m);
        var quote = await free.Returns.QuoteAsync(1, "WrongItem");
        var request = await free.Returns.RequestPricedReturnAsync(
            1,
            new("WrongItem", null, "promotion-free", quote.PricingFingerprint)
        );
        await free.Returns.ApproveAsync(request.Id);
        await free.DeliverReturnAsync();
        await free.Returns.MarkReceivedAsync(request.Id);
        Equal(0, free.Gateway.Amounts.Count, "fully coupon-funded refund remains internal");
        Equal(
            5m,
            free.Account.NegativeBalance,
            "zero buyer payment still charges seller return postage"
        );
        Equal(
            10m,
            free.Settlement.RefundedAmount,
            "zero payment reverses platform funding separately"
        );
    }

    private static async Task GuardsAsync()
    {
        await using var f = await Fixture.CreateAsync(1, 6m, 5m, 0m);
        var q = await f.Returns.QuoteAsync(1, "ChangedMind");
        Check(!q.CanRequest && q.BlockReason != null, "postage above goods amount is blocked");
        await RejectAsync(() =>
            f.Returns.RequestPricedReturnAsync(
                1,
                new("ChangedMind", null, "blocked", q.PricingFingerprint)
            )
        );
        Equal(0, await f.Db.ReturnRequests.CountAsync(), "blocked quote creates no return");
        Equal(
            0,
            await f.Db.ShippingInfos.CountAsync(x => x.Direction == "Return"),
            "blocked quote creates no label"
        );
        var valid = await f.Returns.QuoteAsync(1, "WrongItem");
        await RejectAsync(() =>
            f.Returns.RequestPricedReturnAsync(
                1,
                new("WrongItem", null, "stale", new string('0', 64))
            )
        );
        await RejectAsync(() => f.Returns.QuoteAsync(1, "WrongUnknown"));
        var r = await f.Returns.RequestPricedReturnAsync(
            1,
            new("WrongItem", null, "valid", valid.PricingFingerprint)
        );
        r.BuyerRefundAmount = 999m;
        await RejectAsync(() => f.Db.SaveChangesAsync());
        f.Db.Entry(r).Reload();
        await RejectAsync(() =>
            f.Returns.RequestPricedReturnAsync(
                1,
                new("ChangedMind", null, "valid", valid.PricingFingerprint)
            )
        );
        Check(
            f.Gateway.Amounts.Count == 0 && f.Carrier.Calls == 0,
            "validation failures never call external gateways"
        );
        await using var weight = await Fixture.CreateAsync(1, 105.20m, 5.20m, 0m);
        (await weight.Db.OrderTables.SingleAsync()).TotalWeightKg = 1.2m;
        await weight.Db.SaveChangesAsync();
        Equal(
            5.20m,
            (await weight.Returns.QuoteAsync(1, "ChangedMind")).ReturnShippingFee,
            "fractional weight return pricing"
        );
    }

    private static async Task RetryAsync()
    {
        await using var f = await Fixture.CreateAsync(3);
        var q = await f.Returns.QuoteAsync(1, "ChangedMind");
        var r = await f.Returns.RequestPricedReturnAsync(
            1,
            new("ChangedMind", null, "retry", q.PricingFingerprint)
        );
        f.Carrier.Failures = 3;
        await f.Returns.ApproveAsync(r.Id);
        Equal("Approved", r.Status, "label failure stays retryable");
        Equal(0m, f.Settlement.ReturnShippingCost, "no successful label means no postage charge");
        await f.Returns.RetryShipmentAsync(r.Id);
        await f.DeliverReturnAsync();
        f.Gateway.Failures = 1;
        await f.Returns.MarkReceivedAsync(r.Id);
        Equal("RefundFailed", r.Status, "refund failure remains pending work");
        Equal(87.10m, f.Account.OnHoldBalance, "failed refund keeps funds held after postage");
        f.Clock.Advance(45);
        await f.Returns.MaintainAsync();
        await f.Returns.MaintainAsync();
        Equal("Refunded", r.Status, "worker retries exact quoted refund");
        Check(
            f.Gateway.Amounts.Count == 2 && f.Gateway.Amounts.All(x => x == 85m),
            "all refund attempts use same cash amount"
        );
        Equal(
            1,
            await f.Db.FinancialTransactions.CountAsync(x => x.Type == "ReturnShippingCost"),
            "label retry charges once"
        );
        Equal(4.39m, f.Account.AvailableBalance, "retry releases remainder once");
    }

    private static async Task AdminDecisionAsync()
    {
        await using var f = await Fixture.CreateAsync(3);
        var q = await f.Returns.QuoteAsync(1, "ChangedMind");
        var r = await f.Returns.RequestPricedReturnAsync(
            1,
            new("ChangedMind", null, "issue", q.PricingFingerprint)
        );
        await f.Returns.ApproveAsync(r.Id);
        await f.DeliverReturnAsync();
        var disputes = new DisputeService(f.Db, f.Finance, f.Returns, f.Config, f.Clock);
        var dispute = await disputes.ReportReturnIssueAsync(
            r.Id,
            2,
            new("Seller disputes returned goods", ["https://example.test/evidence"])
        );
        await disputes.ResolveAsync(dispute.Id, 3, new(true, "Full buyer refund by admin"));
        Equal(
            95m,
            f.Gateway.Amounts.Single(),
            "admin full refund overrides buyer postage deduction"
        );
        Equal(5m, f.Account.NegativeBalance, "full buyer win leaves seller return-postage expense");
        Equal(0m, f.Account.OnHoldBalance, "admin full refund consumes hold");
    }

    private static async Task NewAgreementAsync()
    {
        await using var f = await Fixture.CreateAsync(3);
        f.Db.Disputes.Add(
            new Dispute
            {
                OrderId = 1,
                WorkflowEnabled = true,
                IsOpen = true,
                Status = "ExecutingAgreement",
                Proposal = "ReturnRefund",
            }
        );
        await f.Db.SaveChangesAsync();
        var r = await f.Returns.RequestReturnAsync(1, "Accepted full-refund agreement");
        Check(
            r.PricingPolicyVersion == 1 && r.ShippingPayer == "Seller",
            "new agreements account for seller-funded postage"
        );
        await f.Returns.ApproveAsync(r.Id);
        await f.DeliverReturnAsync();
        await f.Returns.MarkReceivedAsync(r.Id);
        Equal(95m, f.Gateway.Amounts.Single(), "full-refund agreement keeps its buyer promise");
        Equal(5m, f.Account.NegativeBalance, "agreement also records carrier expense");
    }

    private static void Check(bool value, string name)
    {
        if (!value)
            throw new Exception(name);
    }

    private static void Equal<T>(T expected, T actual, string name) =>
        Check(
            EqualityComparer<T>.Default.Equals(expected, actual),
            $"{name}: expected {expected}, got {actual}"
        );

    private static async Task RejectAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return;
        }
        throw new Exception("Expected request rejection");
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => now;

        public DateTime Utc => now.UtcDateTime;

        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }

    private sealed class Carrier : ICarrierGateway
    {
        public int Calls,
            Failures;

        public Task<string> CreateLabelAsync(
            int orderId,
            string direction,
            string key,
            CancellationToken ct
        )
        {
            Calls++;
            if (Failures-- > 0)
                throw new HttpRequestException("Carrier temporarily unavailable");
            return Task.FromResult(key);
        }
    }

    private sealed class Gateway : IRefundGateway
    {
        public List<decimal> Amounts = [];
        public int Failures;

        public Task<string> RefundAsync(Payment p, decimal amount, string key, CancellationToken ct)
        {
            Amounts.Add(amount);
            if (Failures-- > 0)
                throw new HttpRequestException("Refund temporarily unavailable");
            return Task.FromResult(key);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db = null!;
        public Clock Clock = new();
        public Carrier Carrier = new();
        public Gateway Gateway = new();
        public IConfiguration Config = null!;
        public SellerFinanceService Finance = null!;
        public ReturnService Returns = null!;
        public SellerSettlement Settlement = null!;
        public SellerAccount Account = null!;

        public static async Task<Fixture> CreateAsync(
            int level,
            decimal paid = 95m,
            decimal shipping = 5m,
            decimal subsidy = 10m
        )
        {
            var f = new Fixture();
            f.Db = new(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );
            f.Config = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Finance:Levels:1:HoldSeconds"] = "30",
                        ["Returns:ReceiptSeconds"] = "45",
                    }
                )
                .Build();
            f.Db.AddRange(
                new User
                {
                    Id = 1,
                    Role = "buyer",
                    Email = "buyer@example.test",
                },
                new User
                {
                    Id = 2,
                    Role = "seller",
                    Email = "seller@example.test",
                },
                new User { Id = 3, Role = "admin" },
                new SellerAccount
                {
                    Id = 1,
                    SellerId = 2,
                    Level = level,
                    MonthlySalesLimit = 1000000m,
                    HoldDays = 21,
                    SalesMonth = new DateTime(f.Clock.Utc.Year, f.Clock.Utc.Month, 1),
                },
                new OrderTable
                {
                    Id = 1,
                    BuyerId = 1,
                    SellerId = 2,
                    Status = "Delivered",
                    TotalPrice = paid,
                    ShippingFee = shipping,
                    Subtotal = paid - shipping + subsidy,
                    PricingSchemaVersion = 1,
                    PlatformSubsidy = subsidy,
                    SellerGrossSnapshot = paid + subsidy,
                    TotalWeightKg = 1m,
                    PickupStateSnapshot = "Hanoi",
                    PickupCountrySnapshot = "Vietnam",
                    DeliveryStateSnapshot = "HCM",
                    DeliveryCountrySnapshot = "Vietnam",
                    PickupAddressSnapshot = "Seller, Road, City, Hanoi, Vietnam",
                    AddressSnapshot = "Buyer, Road, City, HCM, Vietnam",
                },
                new Payment
                {
                    Id = 1,
                    OrderId = 1,
                    Amount = paid,
                    Status = "Succeeded",
                    Method = paid == 0 ? "Promotion" : "Card",
                    PaidAt = f.Clock.Utc,
                },
                new ShippingInfo
                {
                    OrderId = 1,
                    Direction = "Outbound",
                    Status = "Delivered",
                    DeliveredAt = f.Clock.Utc,
                }
            );
            await f.Db.SaveChangesAsync();
            f.Finance = new(f.Db, f.Config, f.Clock);
            f.Settlement = await f.Finance.RecordSuccessfulPaymentAsync(1, 1);
            f.Account = await f.Db.SellerAccounts.SingleAsync();
            f.Returns = new(f.Db, f.Gateway, f.Carrier, f.Finance, f.Config, f.Clock);
            return f;
        }

        public async Task DeliverReturnAsync()
        {
            var shipment = await Db.ShippingInfos.SingleAsync(x => x.Direction == "Return");
            shipment.Status = "Delivered";
            shipment.DeliveredAt = Clock.Utc;
            await Db.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
