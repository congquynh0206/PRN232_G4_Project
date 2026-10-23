using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Shipping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class FinancePolicyChecks
{
    public static async Task RunAsync()
    {
        await DeliveryHoldsAsync(1, 300);
        await DeliveryHoldsAsync(2, 180);
        await InstantWalletAsync();
        await ProductionAndUnconfirmedAsync();
        await FeeChangesAsync();
        await LegacyAsync();
        Console.WriteLine(
            "Finance policy: delivery-based holds, 300/180/0 seconds, Instant wallet, fee effective dates, audit and snapshots passed"
        );
    }

    private static async Task DeliveryHoldsAsync(int level, int seconds)
    {
        await using var f = await Fixture.CreateAsync(level);
        var settlement = await f.Finance.RecordSuccessfulPaymentAsync(1, 1);
        Check(
            settlement.ReleaseAt == null && settlement.HoldSecondsSnapshot == seconds,
            "unshipped order has no release date"
        );
        f.Clock.Advance(3600);
        Check(
            await f.Finance.ReleaseDueFundsAsync() == 0,
            "time spent shipping never counts toward hold"
        );
        f.Db.ShippingInfos.Add(
            new ShippingInfo
            {
                OrderId = 1,
                Direction = "Return",
                Status = "Delivered",
                DeliveredAt = f.Clock.Utc,
            }
        );
        await f.Db.SaveChangesAsync();
        await f.Finance.StartDeliveryHoldAsync(1);
        Check(settlement.ReleaseAt == null, "return delivery cannot start outbound hold");
        var order = await f.Db.OrderTables.SingleAsync();
        order.Status = "Shipping";
        var parcel = new ShippingInfo
        {
            OrderId = 1,
            Direction = "Outbound",
            Status = "OutForDelivery",
            ShipperId = 3,
        };
        f.Db.ShippingInfos.Add(parcel);
        await f.Db.SaveChangesAsync();
        var shipping = new ShipmentService(f.Db, new Carrier(), f.Config, f.Clock);
        await shipping.RecordEventAsync(parcel.Id, "delivered", "Delivered", shipperId: 3);
        Check(
            settlement.HoldStartedAt == f.Clock.Utc
                && settlement.ReleaseAt == f.Clock.Utc.AddSeconds(seconds),
            "tracking starts full snapshotted hold"
        );
        var release = settlement.ReleaseAt;
        f.Clock.Advance(seconds - 1);
        Check(await f.Finance.ReleaseDueFundsAsync() == 0, "hold is not released early");
        Check(
            !await shipping.RecordEventAsync(parcel.Id, "delivered", "Delivered", shipperId: 3),
            "duplicate delivery ignored"
        );
        Check(settlement.ReleaseAt == release, "duplicate delivery never resets hold");
        // A later account level/configuration must not shorten this order's hold.
        (await f.Db.SellerAccounts.SingleAsync()).Level = 3;
        await f.Db.SaveChangesAsync();
        var newerConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Finance:Levels:1:HoldSeconds"] = "0" }
            )
            .Build();
        Check(
            await new SellerFinanceService(f.Db, newerConfig, f.Clock).ReleaseDueFundsAsync() == 0,
            "hold snapshot survives level and config changes"
        );
        f.Clock.Advance(1);
        f.Db.ReturnRequests.Add(new ReturnRequest { OrderId = 1, Status = "Requested" });
        await f.Db.SaveChangesAsync();
        Check(await f.Finance.ReleaseDueFundsAsync() == 0, "active return blocks release");
        (await f.Db.ReturnRequests.SingleAsync()).Status = "Rejected";
        f.Db.Disputes.Add(
            new Dispute
            {
                OrderId = 1,
                WorkflowEnabled = true,
                IsOpen = true,
            }
        );
        await f.Db.SaveChangesAsync();
        Check(await f.Finance.ReleaseDueFundsAsync() == 0, "active dispute blocks release");
        (await f.Db.Disputes.SingleAsync()).IsOpen = false;
        await f.Db.SaveChangesAsync();
        Check(
            await f.Finance.ReleaseDueFundsAsync() == 1,
            "hold releases at delivery plus duration"
        );
        Check(
            (await f.Db.SellerAccounts.SingleAsync()).AvailableBalance == 87.70m,
            "wallet gets net after fees"
        );
        Check(await f.Finance.ReleaseDueFundsAsync() == 0, "worker cannot credit twice");
    }

    private static async Task InstantWalletAsync()
    {
        await using var f = await Fixture.CreateAsync(3);
        var account = await f.Db.SellerAccounts.SingleAsync();
        account.NegativeBalance = 40m;
        await f.Db.SaveChangesAsync();
        var settlement = await f.Finance.RecordSuccessfulPaymentAsync(1, 1);
        Check(
            account.AvailableBalance == 47.70m
                && account.ProcessingBalance == 0
                && account.NegativeBalance == 0,
            "Instant recovers debt then credits wallet"
        );
        Check(
            settlement.Status == "Available"
                && settlement.HoldSecondsSnapshot == 0
                && settlement.ReleaseAt == f.Clock.Utc,
            "Instant has no delivery requirement"
        );
        Check(!await f.Db.SellerPayouts.AnyAsync(), "wallet credit is not a bank payout");
        await f.Finance.RecordSuccessfulPaymentAsync(1, 1);
        Check(
            account.AvailableBalance == 47.70m
                && await f.Db.FinancialTransactions.CountAsync(x => x.Type == "FundsReleased") == 1,
            "payment retries credit wallet once"
        );
        await f.Finance.PlaceHoldAsync(1, "test hold");
        Check(
            account.AvailableBalance == 0 && account.OnHoldBalance == 47.70m,
            "Instant wallet funds remain holdable"
        );
        await f.Finance.ResolveHoldAsync(1, true);
        Check(
            account.AvailableBalance == 47.70m && account.OnHoldBalance == 0,
            "seller-win restores Instant wallet without delivery"
        );
        await f.Finance.ApplyShippingCostAsync(1, 7, 2m);
        Check(
            account.AvailableBalance == 45.70m && account.NegativeBalance == 0,
            "Instant shipping cost debits available wallet before debt"
        );
        var refund = new Refund
        {
            OrderId = 1,
            PaymentId = 1,
            Amount = 100m,
            Reason = "cancel",
            Status = "Succeeded",
            IdempotencyKey = "instant-refund",
        };
        f.Db.Refunds.Add(refund);
        await f.Db.SaveChangesAsync();
        await f.Finance.ApplyRefundAsync(1, refund.Id);
        var negative = account.NegativeBalance;
        Check(
            account.AvailableBalance == 0 && account.OnHoldBalance == 0 && negative == 42m,
            "refund correctly recovers Instant proceeds"
        );
        await f.Finance.ApplyRefundAsync(1, refund.Id);
        Check(account.NegativeBalance == negative, "refund retry never charges twice");
    }

    private static async Task FeeChangesAsync()
    {
        await using var f = await Fixture.CreateAsync(1);
        var fees = new PlatformFeeService(f.Db, f.Clock);
        var old = await f.Finance.RecordSuccessfulPaymentAsync(1, 1);
        var schedule = new PlatformFeeChangeRequest(
            15m,
            .50m,
            f.Clock.GetUtcNow().AddHours(1),
            "<script>audit</script>",
            "fee-schedule",
            1
        );
        var id = await fees.ChangeAsync(4, schedule);
        Check(await fees.ChangeAsync(4, schedule) == id, "configuration request is idempotent");
        Check(
            (await fees.ReadAsync(4)).Current.FeePercent == 12m,
            "future fee does not apply early"
        );
        await RejectAsync(
            () => fees.ChangeAsync(2, schedule),
            typeof(UnauthorizedAccessException),
            "seller cannot change fees"
        );
        await RejectAsync(
            () => fees.ReadAsync(2),
            typeof(UnauthorizedAccessException),
            "seller cannot read admin audit"
        );
        await RejectAsync(
            () => fees.ChangeAsync(4, schedule with { RequestKey = "stale" }),
            typeof(InvalidOperationException),
            "stale admin cannot overwrite config"
        );
        await RejectAsync(
            () =>
                fees.ChangeAsync(
                    4,
                    schedule with
                    {
                        RequestKey = "past",
                        ExpectedLatestPolicyId = id,
                        EffectiveAt = f.Clock.GetUtcNow().AddSeconds(-1),
                    }
                ),
            typeof(ArgumentException),
            "retroactive fee forbidden"
        );
        await RejectAsync(
            () => fees.ChangeAsync(4, schedule with { FeePercent = 101m }),
            typeof(ArgumentException),
            "invalid fee rejected"
        );
        await RejectAsync(
            () => fees.ChangeAsync(4, schedule with { FixedFee = -.1m }),
            typeof(ArgumentException),
            "negative fixed fee rejected"
        );
        await RejectAsync(
            () => fees.ChangeAsync(4, schedule with { Reason = " " }),
            typeof(ArgumentException),
            "audit reason required"
        );
        await RejectAsync(
            () => fees.ChangeAsync(4, schedule with { FeePercent = 16m }),
            typeof(InvalidOperationException),
            "same request key cannot change amount"
        );
        f.Clock.Advance(3600);
        await f.AddPaymentAsync(2, f.Clock.Utc);
        var newer = await f.Finance.RecordSuccessfulPaymentAsync(2, 2);
        Check(
            newer.PlatformFeeAmount == 15.50m && newer.FeePolicyId == id,
            "new fee applies exactly at effective time"
        );
        Check(
            old.PlatformFeeAmount == 12.30m && old.FeePercentSnapshot == 12m,
            "old settlement keeps fee snapshot"
        );
        await f.AddPaymentAsync(3, f.Clock.Utc.AddSeconds(-1));
        Check(
            (await f.Finance.RecordSuccessfulPaymentAsync(3, 3)).PlatformFeeAmount == 12.30m,
            "late reconciliation uses original successful payment time"
        );
        var current = await fees.ReadAsync(4);
        Check(
            current.Current.Id == id
                && current.Items[0].ChangedById == 4
                && current.Items[0].PreviousFeePercent == 12m,
            "audit includes actor old/new and effective time"
        );
        f.Clock.Advance(1);
        var immediate = await fees.ChangeAsync(
            4,
            new(100m, .30m, null, "Immediate fee cap", "fee-now", id)
        );
        await f.AddPaymentAsync(4, f.Clock.Utc);
        Check(
            (await f.Finance.RecordSuccessfulPaymentAsync(4, 4)).PlatformFeeAmount == 100m,
            "fee never exceeds seller gross"
        );
        var refund = new Refund
        {
            OrderId = 1,
            PaymentId = 1,
            Amount = 100m,
            Reason = "cancel",
            Status = "Succeeded",
            IdempotencyKey = "old-fee-refund",
        };
        f.Db.Refunds.Add(refund);
        await f.Db.SaveChangesAsync();
        await f.Finance.ApplyRefundAsync(1, refund.Id);
        Check(old.FeeCreditAmount == 12.30m, "refund credits original fee after policy changes");
        var row = await f.Db.PlatformFeePolicies.SingleAsync(x => x.Id == immediate);
        row.Reason = "tampered";
        await RejectAsync(
            () => f.Db.SaveChangesAsync(),
            typeof(InvalidOperationException),
            "audit update forbidden"
        );
        f.Db.Entry(row).State = EntityState.Unchanged;
        f.Db.Remove(row);
        await RejectAsync(
            () => f.Db.SaveChangesAsync(),
            typeof(InvalidOperationException),
            "audit deletion forbidden"
        );
    }

    private static async Task LegacyAsync()
    {
        await using var f = await Fixture.CreateAsync(1);
        var legacy = new SellerSettlement
        {
            OrderId = 1,
            PaymentId = 1,
            SellerAccountId = 1,
            SellerId = 2,
            GrossAmount = 100m,
            PlatformFeeAmount = 12.30m,
            NetAmount = 87.70m,
            ProcessingAmount = 87.70m,
            ReleaseAt = f.Clock.Utc.AddSeconds(-1),
            CreatedAt = f.Clock.Utc.AddDays(-21),
        };
        f.Db.SellerSettlements.Add(legacy);
        (await f.Db.SellerAccounts.SingleAsync()).ProcessingBalance = 87.70m;
        (await f.Db.OrderTables.SingleAsync()).Status = "Delivered";
        await f.Db.SaveChangesAsync();
        var originalRelease = legacy.ReleaseAt;
        await f.Finance.StartDeliveryHoldAsync(1);
        Check(
            legacy.HoldPolicyVersion == 0 && legacy.ReleaseAt == originalRelease,
            "legacy fee and schedule are preserved"
        );
        Check(
            await f.Finance.ReleaseDueFundsAsync() == 1,
            "legacy settlement continues to release"
        );
    }

    private static async Task ProductionAndUnconfirmedAsync()
    {
        foreach (var level in new[] { 1, 2 })
        {
            await using var f = await Fixture.CreateAsync(level);
            var production = new SellerFinanceService(
                f.Db,
                new ConfigurationBuilder().Build(),
                f.Clock
            );
            var settlement = await production.RecordSuccessfulPaymentAsync(1, 1);
            Check(
                settlement.HoldSecondsSnapshot == (level == 1 ? 21 : 7) * 86400,
                "production uses 21/7 days"
            );
            f.Clock.Advance(60);
            (await f.Db.OrderTables.SingleAsync()).Status = "Delivered";
            f.Db.ShippingInfos.Add(
                new ShippingInfo
                {
                    OrderId = 1,
                    Direction = "Outbound",
                    Status = "Delivered",
                    DeliveredAt = f.Clock.Utc,
                }
            );
            await f.Db.SaveChangesAsync();
            await production.StartDeliveryHoldAsync(1);
            Check(
                settlement.ReleaseAt == f.Clock.Utc.AddDays(level == 1 ? 21 : 7),
                "production duration starts at actual delivery"
            );
        }
        await using var uncertain = await Fixture.CreateAsync(3);
        (await uncertain.Db.Payments.SingleAsync()).Status = "Verifying";
        await uncertain.Db.SaveChangesAsync();
        await RejectAsync(
            () => uncertain.Finance.RecordSuccessfulPaymentAsync(1, 1),
            typeof(InvalidOperationException),
            "unconfirmed payment cannot credit Instant wallet"
        );
        Check(
            !await uncertain.Db.SellerSettlements.AnyAsync()
                && (await uncertain.Db.SellerAccounts.SingleAsync()).AvailableBalance == 0,
            "verification leaves wallet untouched"
        );
    }

    private static void Check(bool value, string name)
    {
        if (!value)
            throw new Exception(name);
    }

    private static async Task RejectAsync(Func<Task> action, Type type, string name)
    {
        try
        {
            await action();
        }
        catch (Exception e) when (type.IsInstanceOfType(e))
        {
            return;
        }
        throw new Exception(name);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
        public DateTime Utc => now.UtcDateTime;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db = null!;
        public Clock Clock = new();
        public IConfiguration Config = null!;
        public SellerFinanceService Finance = null!;

        public static async Task<Fixture> CreateAsync(int level)
        {
            var f = new Fixture();
            f.Db = new(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );
            await f.Db.Database.EnsureCreatedAsync();
            f.Config = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Finance:Levels:1:HoldSeconds"] = "300",
                        ["Finance:Levels:2:HoldSeconds"] = "180",
                    }
                )
                .Build();
            f.Db.Users.AddRange(
                new User { Id = 1, Role = "buyer" },
                new User { Id = 2, Role = "seller" },
                new User { Id = 3, Role = "shipper" },
                new User
                {
                    Id = 4,
                    Role = "admin",
                    Username = "Admin test",
                }
            );
            f.Db.SellerAccounts.Add(
                new SellerAccount
                {
                    Id = 1,
                    SellerId = 2,
                    Level = level,
                    HoldDays =
                        level == 1 ? 21
                        : level == 2 ? 7
                        : 0,
                    MonthlySalesLimit = 1000000m,
                    SalesMonth = new DateTime(2026, 10, 1),
                }
            );
            await f.Db.SaveChangesAsync();
            await f.AddPaymentAsync(1, f.Clock.Utc);
            f.Finance = new(f.Db, f.Config, f.Clock);
            return f;
        }

        public async Task AddPaymentAsync(int id, DateTime paidAt)
        {
            Db.OrderTables.Add(
                new OrderTable
                {
                    Id = id,
                    BuyerId = 1,
                    SellerId = 2,
                    Status = "Paid",
                    TotalPrice = 100m,
                    OrderDate = paidAt,
                }
            );
            Db.Payments.Add(
                new Payment
                {
                    Id = id,
                    OrderId = id,
                    Amount = 100m,
                    Status = "Succeeded",
                    Method = "Card",
                    PaidAt = paidAt,
                }
            );
            await Db.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class Carrier : G4.Application.Integrations.ICarrierGateway
    {
        public Task<string> CreateLabelAsync(
            int orderId,
            string direction,
            string key,
            CancellationToken ct
        ) => Task.FromResult(key);
    }
}
