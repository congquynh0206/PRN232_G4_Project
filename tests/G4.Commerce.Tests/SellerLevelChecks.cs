using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class SellerLevelChecks
{
    internal static void Check(bool ok, string message) =>
        SellerRegistrationChecks.Check(ok, message);

    internal static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Finance:LevelPromotion:EvaluationSeconds"] = "60",
                    ["Finance:Levels:1:HoldSeconds"] = "300",
                    ["Finance:Levels:2:HoldSeconds"] = "180",
                }
            )
            .Build();

    public static async Task RunAsync()
    {
        Rules();
        await NewSeller();
        await Feedback();
        await WorkerAndSnapshots();
        await Metrics();
        Console.WriteLine(
            "Seller levels: rule boundaries, real feedback, rolling metrics, worker timing, history and old financial snapshots passed"
        );
    }

    private static void Rules()
    {
        var p = SellerLevelRules.Policy(Config());
        var now = new DateTime(2026, 10, 15);
        var m = new SellerLevelMetrics(
            100,
            100,
            100,
            100,
            100,
            100,
            0,
            0,
            0,
            90,
            0,
            false,
            now.AddDays(-90),
            now
        );
        Check(
            SellerLevelRules.Decide(1, m, p).ResultLevel == 3,
            "qualified seller can advance directly to level 3"
        );
        var boundary = m with
        {
            CompletedOrders = 20,
            PositiveFeedbackRate = 90,
            OnTimeDeliveryRate = 90,
            ActivityDays = 30,
            CancellationRate = 5,
            ReturnRate = 10,
            DisputeRate = 2,
            PaidOrders = 20,
        };
        Check(
            SellerLevelRules.Decide(1, boundary, p).ResultLevel == 2,
            "inclusive level 2 boundaries"
        );
        Check(
            SellerLevelRules
                .Decide(2, boundary with { PositiveFeedbackRate = 89.999m }, p)
                .ResultLevel == 1,
            "decisions use unrounded rates"
        );
        Check(
            SellerLevelRules.Decide(1, m with { ActivityDays = 89 }, p).ResultLevel == 2,
            "level 3 activity age boundary"
        );
        Check(
            SellerLevelRules
                .Decide(3, m with { PaidOrders = 19, CancellationRate = 100 }, p)
                .ResultLevel == 3,
            "small sample keeps current level"
        );
        Check(
            SellerLevelRules
                .Decide(3, m with { PaidOrders = 0, NegativeBalance = .01m }, p)
                .ResultLevel == 1,
            "debt overrides minimum sample"
        );
        Check(
            SellerLevelRules.Decide(3, m with { Restricted = true }, p).ResultLevel == 1,
            "restriction overrides performance"
        );
        var unknown = m with
        {
            FeedbackCount = 0,
            PositiveFeedbackRate = null,
            DeliveryCount = 0,
            OnTimeDeliveryRate = null,
        };
        Check(
            SellerLevelRules.Decide(1, unknown, p).ResultLevel == 1
                && SellerLevelRules.Decide(3, unknown, p).ResultLevel == 3,
            "unknown metrics neither qualify promotion nor become negative ratings for retention"
        );
        var bad = Config();
        bad["Finance:LevelPromotion:Level3:MaxCancellationRate"] = "50";
        try
        {
            SellerLevelRules.Policy(bad);
            throw new Exception("Invalid policy accepted");
        }
        catch (InvalidOperationException) { }
    }

    private static async Task NewSeller()
    {
        await using var f = new Fixture();
        var seller = new User { Role = "seller", Email = "new-level@example.test" };
        f.Db.Users.Add(seller);
        await f.Db.SaveChangesAsync();
        Check(
            await f.Finance.MaintainLevelsAsync() == 1,
            "new seller with no account/payments is evaluated without error"
        );
        var d = await f.Finance.GetLevelDetailsAsync(seller.Id);
        Check(
            d.Level == 1
                && d.Preview.Metrics.PaidOrders == 0
                && d.Preview.Metrics.PositiveFeedbackRate == null
                && !d.Preview.Metrics.Restricted
                && d.History.Count == 1,
            "new seller keeps level 1 and has no fabricated negative feedback or restriction"
        );
    }

    internal static async Task<(int Seller, int Buyer, int[] Orders)> Seed(
        ApplicationDbContext db,
        DateTime now,
        int count,
        bool feedback
    )
    {
        var seller = new User
        {
            Role = "seller",
            Username = "Level QA seller",
            Email = $"level-{Guid.NewGuid():N}@example.test",
        };
        var buyer = new User
        {
            Role = "buyer",
            Username = "Level QA buyer",
            Email = $"buyer-{Guid.NewGuid():N}@example.test",
        };
        db.Users.AddRange(seller, buyer);
        await db.SaveChangesAsync();
        db.SellerAccounts.Add(
            new SellerAccount
            {
                SellerId = seller.Id,
                Level = 1,
                Status = "Active",
                MonthlySalesLimit = 5000,
                HoldDays = 21,
                SalesMonth = new DateTime(now.Year, now.Month, 1),
                MonthlySalesAmount = 123,
                AvailableBalance = 5,
                UpdatedAt = now,
                LevelActivityStartedAt = now.AddDays(-100),
            }
        );
        var orders = Enumerable
            .Range(0, count)
            .Select(i => new OrderTable
            {
                SellerId = seller.Id,
                BuyerId = buyer.Id,
                Status = "Delivered",
                TotalPrice = 10,
                OrderDate = now.AddDays(-10),
                UpdatedAt = now,
            })
            .ToArray();
        db.OrderTables.AddRange(orders);
        await db.SaveChangesAsync();
        foreach (var o in orders)
        {
            db.Payments.Add(
                new Payment
                {
                    OrderId = o.Id,
                    UserId = buyer.Id,
                    Status = "Succeeded",
                    Amount = 10,
                    Method = "Card",
                    CreatedAt = now.AddDays(-10),
                    PaidAt = now.AddDays(-10),
                }
            );
            db.ShippingInfos.Add(
                new ShippingInfo
                {
                    OrderId = o.Id,
                    Direction = "Outbound",
                    Status = "Delivered",
                    EstimatedArrival = now.AddDays(-8),
                    DeliveredAt = now.AddDays(-9),
                    CreatedAt = now.AddDays(-10),
                    DeliveryAttempts = 2,
                }
            );
            if (feedback)
                db.SellerOrderFeedbacks.Add(
                    new SellerOrderFeedback
                    {
                        OrderId = o.Id,
                        SellerId = seller.Id,
                        BuyerId = buyer.Id,
                        Rating = 5,
                        Comment = "QA",
                        RequestKey = Guid.NewGuid().ToString(),
                        CreatedAt = now.AddDays(-9),
                    }
                );
        }
        await db.SaveChangesAsync();
        return (seller.Id, buyer.Id, orders.Select(x => x.Id).ToArray());
    }

    private static async Task Feedback()
    {
        await using var f = new Fixture();
        var s = await Seed(f.Db, f.Clock.Utc, 3, false);
        var service = new SellerFeedbackService(f.Db, f.Clock);
        await SellerRegistrationChecks.Reject<UnauthorizedAccessException>(() =>
            service.SubmitAsync(s.Orders[0], s.Buyer + 1000, new(5, "", "wrong"))
        );
        await SellerRegistrationChecks.Reject<ArgumentException>(() =>
            service.SubmitAsync(s.Orders[0], s.Buyer, new(6, "", "bad"))
        );
        var first = await service.SubmitAsync(s.Orders[0], s.Buyer, new(4, " Good ", "once"));
        Check(
            first.Id == (await service.SubmitAsync(s.Orders[0], s.Buyer, new(4, "Good", "once"))).Id
                && first.Comment == "Good",
            "feedback retry same normalized payload returns original"
        );
        await SellerRegistrationChecks.Reject<InvalidOperationException>(() =>
            service.SubmitAsync(s.Orders[0], s.Buyer, new(1, "bad", "once"))
        );
        await SellerRegistrationChecks.Reject<InvalidOperationException>(() =>
            service.SubmitAsync(s.Orders[1], s.Buyer, new(4, "Good", "once"))
        );
        var o = await f.Db.OrderTables.SingleAsync(x => x.Id == s.Orders[1]);
        o.Status = "Paid";
        await f.Db.SaveChangesAsync();
        await SellerRegistrationChecks.Reject<InvalidOperationException>(() =>
            service.SubmitAsync(o.Id, s.Buyer, new(5, "", "early"))
        );
        o.Status = "Closed";
        var payment = await f.Db.Payments.SingleAsync(x => x.OrderId == o.Id);
        payment.Status = "Failed";
        await f.Db.SaveChangesAsync();
        await SellerRegistrationChecks.Reject<InvalidOperationException>(() =>
            service.SubmitAsync(o.Id, s.Buyer, new(5, "", "unpaid"))
        );
        payment.Status = "Succeeded";
        await f.Db.SaveChangesAsync();
        await service.SubmitAsync(o.Id, s.Buyer, new(3, "Neutral", "second"));
        var m = (await f.Finance.GetLevelDetailsAsync(s.Seller)).Preview.Metrics;
        Check(
            m.FeedbackCount == 2 && m.PositiveFeedbackRate == 50m,
            "missing third feedback excluded; four stars positive, three not positive"
        );
        (await f.Db.SellerOrderFeedbacks.FirstAsync()).Rating = 1;
        await SellerRegistrationChecks.Reject<InvalidOperationException>(() =>
            f.Db.SaveChangesAsync()
        );
    }

    private static async Task WorkerAndSnapshots()
    {
        await using var f = new Fixture();
        var s = await Seed(f.Db, f.Clock.Utc, 100, true);
        var firstPayment = await f.Db.Payments.SingleAsync(x => x.OrderId == s.Orders[0]);
        var old = await f.Finance.RecordSuccessfulPaymentAsync(s.Orders[0], firstPayment.Id);
        var oldRelease = old.ReleaseAt;
        Check(
            old.SellerLevelSnapshot == 1 && old.HoldSecondsSnapshot == 300,
            "old settlement snapshots level 1"
        );
        Check(
            await f.Finance.MaintainLevelsAsync() == 1
                && await f.Finance.MaintainLevelsAsync() == 0,
            "worker evaluates only due sellers"
        );
        var a = await f.Db.SellerAccounts.SingleAsync();
        Check(
            a.Level == 3
                && a.MonthlySalesLimit == 1000000
                && a.MonthlySalesAmount == 133
                && a.AvailableBalance == 5,
            "automatic promotion updates policy without moving money or resetting sales"
        );
        Check(
            old.SellerLevelSnapshot == 1
                && old.ReleaseAt == oldRelease
                && old.HoldSecondsSnapshot == 300,
            "promotion leaves old schedule intact"
        );
        var secondPayment = await f.Db.Payments.SingleAsync(x => x.OrderId == s.Orders[1]);
        var instant = await f.Finance.RecordSuccessfulPaymentAsync(s.Orders[1], secondPayment.Id);
        Check(
            instant.SellerLevelSnapshot == 3
                && instant.HoldSecondsSnapshot == 0
                && instant.ReleasedAt != null
                && a.AvailableBalance > 5,
            "new settlement uses instant policy after promotion"
        );
        var frozen = (await f.Finance.GetLevelDetailsAsync(s.Seller)).History.Single().Decision;
        f.Clock.Advance(59);
        Check(await f.Finance.MaintainLevelsAsync() == 0, "no early evaluation");
        f.Clock.Advance(1);
        Check(await f.Finance.MaintainLevelsAsync() == 1, "evaluation at exact interval");
        foreach (var o in await f.Db.OrderTables.OrderBy(x => x.Id).Take(20).ToListAsync())
            o.Status = "Cancelled";
        await f.Db.SaveChangesAsync();
        await f.Finance.EvaluateLevelAsync(s.Seller);
        Check(
            a.Level == 1 && a.Status == "Active",
            "bad performance demotes but does not ban seller"
        );
        Check(
            frozen.Metrics.CancellationRate == 0 && frozen.ResultLevel == 3,
            "historical metrics do not follow live changes"
        );
        a.Level = 3;
        a.NegativeBalance = 1;
        await f.Db.SaveChangesAsync();
        await f.Finance.EvaluateLevelAsync(s.Seller);
        Check(a.Level == 1 && a.NegativeBalance == 1, "debt forces level 1 without forgiving debt");
        var history = await f.Db.SellerLevelEvaluations.FirstAsync();
        history.Reason = "tampered";
        await SellerRegistrationChecks.Reject<InvalidOperationException>(() =>
            f.Db.SaveChangesAsync()
        );
    }

    private static async Task Metrics()
    {
        await using var f = new Fixture();
        var s = await Seed(f.Db, f.Clock.Utc, 20, false);
        f.Db.Feedbacks.Add(
            new Feedback
            {
                SellerId = s.Seller,
                PositiveRate = 100,
                TotalReviews = 999,
            }
        );
        var orders = await f.Db.OrderTables.OrderBy(x => x.Id).ToArrayAsync();
        orders[0].Status = "Cancelled";
        var outside = await f.Db.Payments.SingleAsync(x => x.OrderId == orders[19].Id);
        outside.PaidAt = f.Clock.Utc.AddDays(-91);
        var missing = await f.Db.ShippingInfos.SingleAsync(x => x.OrderId == orders[18].Id);
        missing.EstimatedArrival = null;
        var late = await f.Db.ShippingInfos.SingleAsync(x => x.OrderId == orders[17].Id);
        late.DeliveredAt = f.Clock.Utc;
        f.Db.Disputes.AddRange(
            new Dispute { OrderId = orders[1].Id, WorkflowEnabled = true },
            new Dispute { OrderId = orders[1].Id, WorkflowEnabled = true }
        );
        f.Db.ReturnRequests.AddRange(
            new ReturnRequest { OrderId = orders[2].Id, Status = "Approved" },
            new ReturnRequest { OrderId = orders[3].Id, Status = "Rejected" }
        );
        f.Db.ShippingInfos.Add(
            new ShippingInfo
            {
                OrderId = orders[16].Id,
                Direction = "Return",
                Status = "Delivered",
                EstimatedArrival = f.Clock.Utc,
                DeliveredAt = f.Clock.Utc,
            }
        );
        await f.Db.SaveChangesAsync();
        var m = (await f.Finance.GetLevelDetailsAsync(s.Seller)).Preview.Metrics;
        Check(
            m.PaidOrders == 19
                && m.CompletedOrders == 20
                && m.FeedbackCount == 0
                && m.PositiveFeedbackRate == null,
            "rolling payment cohort, lifetime delivered proof survives later status changes; legacy aggregate ignored"
        );
        Check(
            m.DeliveryCount == 18 && m.OnTimeDeliveryRate == 17 * 100m / 18,
            "delivery uses actual deadline, excludes missing/return shipping; attempts do not decide lateness"
        );
        Check(
            m.CancellationRate == 100m / 19
                && m.ReturnRate == 100m / 19
                && m.DisputeRate == 100m / 19,
            "rates count distinct paid orders, accepted returns and duplicate disputes once"
        );
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db = new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        public SellerRegistrationChecks.Clock Clock = new();
        public SellerFinanceService Finance;

        public Fixture()
        {
            Db.Database.EnsureCreated();
            Finance = new(Db, Config(), Clock);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
