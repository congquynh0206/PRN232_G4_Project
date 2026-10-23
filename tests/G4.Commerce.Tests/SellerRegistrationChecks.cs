using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Authentication;
using G4.Infrastructure.Checkout;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

internal static class SellerRegistrationChecks
{
    public static async Task RunAsync()
    {
        await Registration();
        await IdentityChanges();
        await AutomaticWriteOff();
        await LegacyGrace();
        await KeyAndEnvironment();
        Console.WriteLine(
            "Seller registration, mock verification, identity privacy, automatic write-off, blacklist and recovery checks passed"
        );
    }

    internal static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["SellerIdentity:HmacKey"] = Convert.ToBase64String(
                        Enumerable.Range(1, 32).Select(x => (byte)x).ToArray()
                    ),
                    ["Finance:Debt:DueSeconds"] = "60",
                    ["Finance:Debt:WarningBeforeSeconds"] = "15",
                    ["Finance:WriteOff:AfterSeconds"] = "300",
                    ["Finance:WriteOff:InactivitySeconds"] = "60",
                }
            )
            .Build();

    internal static SellerRegistrationInput Input(
        string email = "new@example.test",
        string value = "999111111111",
        string key = "registration"
    ) =>
        new(
            "New seller",
            email,
            "SellerDemo123!",
            "SellerDemo123!",
            "New shop",
            new("CCCD", value),
            key
        );

    internal static void Check(bool ok, string message)
    {
        if (!ok)
            throw new Exception(message);
    }

    internal static async Task<T> Reject<T>(Func<Task> work)
        where T : Exception
    {
        try
        {
            await work();
        }
        catch (T e)
        {
            return e;
        }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static async Task Registration()
    {
        await using var f = new Fixture();
        await f.Init();
        var input = Input(" NEW@EXAMPLE.TEST ") with
        {
            Username = "  New seller ",
            StoreName = " New shop ",
            Identity = new("cccd", "999 111 111 111"),
        };
        var result = await f.Service.RegisterAsync(input);
        var a = await f.Db.SellerAccounts.SingleAsync();
        Check(
            result.Status == "Active"
                && result.Email == "new@example.test"
                && a.Level == 1
                && a.Status == "Active"
                && a.PermanentSalesBlockedAt == null
                && a.AvailableBalance == 0
                && a.NegativeBalance == 0
                && a.ProcessingBalance == 0
                && a.OnHoldBalance == 0,
            "new clean seller activates immediately with zero balances and no ban"
        );
        Check(
            await f.Db.Stores.CountAsync() == 1
                && await f.Db.Users.CountAsync() == 1
                && await f.Db.SellerIdentities.CountAsync() == 1,
            "registration creates user, shop, identity and wallet together"
        );
        Check(
            (await f.Service.RegisterAsync(input)).SellerId == result.SellerId
                && await f.Db.Users.CountAsync() == 1,
            "registration retry returns original without duplicate account"
        );
        await Reject<InvalidOperationException>(() =>
            f.Service.RegisterAsync(input with { StoreName = "Other shop" })
        );
        await Reject<ArgumentException>(() =>
            f.Service.RegisterAsync(Input("invalid@example.test", "1234", "invalid"))
        );
        await Reject<ArgumentException>(() =>
            f.Service.RegisterAsync(Input() with { ConfirmPassword = "wrong" })
        );
        var denied = await Reject<UnauthorizedAccessException>(() =>
            f.Service.RegisterAsync(Input("blocked@example.test", "999 000 000 002", "blocked"))
        );
        Check(
            denied.Message == SellerRegistrationService.Denied
                && await f.Db.Users.CountAsync() == 1,
            "blacklist checked before creating account, generic rejection"
        );
        await Reject<UnauthorizedAccessException>(() =>
            f.Service.RegisterAsync(
                Input("duplicate-id@example.test", "999111111111", "duplicate-id")
            )
        );
        await Reject<InvalidOperationException>(() =>
            f.Service.RegisterAsync(Input("NEW@EXAMPLE.TEST", "999222222222", "duplicate-email"))
        );
        var identity = await f.Db.SellerIdentities.SingleAsync();
        Check(
            !identity.MaskedValue.Contains("999111111111") && identity.Fingerprint.Length == 64,
            "raw identity not stored or displayed"
        );
        var user = await new AuthenticationService(f.Db).ValidateCredentialsAsync(
            "NEW@example.test",
            input.Password
        );
        Check(
            user?.Role == "seller" && user.Password != input.Password,
            "registered seller can log in with salted PBKDF2 password"
        );
        var bankA = Input("bank-a@example.test", key: "bank-a") with
        {
            Identity = new("BANK", "000 123 456", " abc "),
        };
        var bankSeller = await f.Service.RegisterAsync(bankA);
        var bankWallet = await f.Db.SellerAccounts.SingleAsync(x =>
            x.SellerId == bankSeller.SellerId
        );
        bankWallet.AvailableBalance = 5m;
        await f.Db.SaveChangesAsync();
        var payout = await f.Finance.RequestPayoutAsync(
            bankSeller.SellerId,
            1m,
            "bank-identity-payout",
            false
        );
        Check(
            payout.DestinationMasked == "ABC · ****3456",
            "payout uses the latest verified bank identity with masked destination"
        );
        await Reject<UnauthorizedAccessException>(() =>
            f.Service.RegisterAsync(
                bankA with
                {
                    Email = "bank-dupe@example.test",
                    Key = "bank-dupe",
                    Identity = new("bank", "000123456", "ABC"),
                }
            )
        );
        await f.Service.RegisterAsync(
            bankA with
            {
                Email = "bank-b@example.test",
                Key = "bank-b",
                Identity = new("BANK", "000123456", "XYZ"),
            }
        );
        Check(
            await f.Db.Users.CountAsync() == 3,
            "bank code participates in fingerprint; leading zeros preserved"
        );
    }

    private static async Task IdentityChanges()
    {
        await using var f = new Fixture();
        await f.Init();
        var seller = await f.Service.RegisterAsync(Input());
        var update = new SellerIdentityChangeInput(new("PASSPORT", "ab 00123456"), 1, "update");
        var profile = await f.Service.ChangeIdentityAsync(seller.SellerId, update);
        Check(
            profile.Version == 2
                && profile.Items.Count == 2
                && profile.Items.Count(x => x.Current) == 1,
            "identity update keeps immutable history and switches current identity"
        );
        Check(
            (await f.Service.ChangeIdentityAsync(seller.SellerId, update)).Version == 2,
            "identity update retry is idempotent"
        );
        await Reject<InvalidOperationException>(() =>
            f.Service.ChangeIdentityAsync(
                seller.SellerId,
                new(new("CCCD", "999333333333"), 1, "stale")
            )
        );
        await Reject<UnauthorizedAccessException>(() =>
            f.Service.ChangeIdentityAsync(
                seller.SellerId,
                new(new("CCCD", "999000000002"), 2, "blocked-change")
            )
        );
        Check(
            (await f.Service.ProfileAsync(seller.SellerId)).Version == 2,
            "blacklisted change does not alter profile"
        );
        var saved = await f.Db.SellerIdentities.FirstAsync();
        saved.MaskedValue = "tampered";
        await Reject<InvalidOperationException>(() => f.Db.SaveChangesAsync());
    }

    private static async Task AutomaticWriteOff()
    {
        await using var f = new Fixture();
        await f.Init();
        var seller = await f.Service.RegisterAsync(Input());
        var account = await f.Db.SellerAccounts.SingleAsync();
        account.NegativeBalance = 40m;
        await f.Db.SaveChangesAsync();
        var first = await f.Finance.GetDebtAsync(seller.SellerId);
        Check(
            first.EarliestWriteOffAt == first.StartedAt!.Value.AddSeconds(300),
            "seller sees earliest automatic restriction date before decision"
        );
        f.Config["Finance:WriteOff:AfterSeconds"] = "600";
        f.Config["Finance:WriteOff:InactivitySeconds"] = "120";
        f.Clock.Advance(299);
        Check(await f.Finance.MaintainWriteOffsAsync() == 0, "no write-off before age threshold");
        await f.Finance.RepayDebtAsync(seller.SellerId, new(10m, 40m, 1, "partial"));
        Check(
            (await f.Finance.GetDebtAsync(seller.SellerId)).EarliestWriteOffAt
                == f.Clock.Utc.AddSeconds(60),
            "recent repayment moves visible earliest write-off date by inactivity grace"
        );
        f.Clock.Advance(1);
        Check(
            await f.Finance.MaintainWriteOffsAsync() == 0,
            "recent repayment delays write-off without extending original debt deadline"
        );
        Check(
            account.DebtDueAt == first.DueAt
                && (await f.Db.SellerDebtCycles.SingleAsync()).WriteOffEligibleAt
                    == first.StartedAt!.Value.AddSeconds(300),
            "both deadlines snapshot original cycle"
        );
        f.Clock.Advance(59);
        Check(
            await f.Finance.MaintainWriteOffsAsync() == 1,
            "worker writes off at exact inactivity boundary"
        );
        Check(
            await f.Finance.MaintainWriteOffsAsync() == 0,
            "worker retry cannot create duplicate decision"
        );
        var decision = await f.Db.SellerDebtWriteOffs.SingleAsync();
        Check(
            decision.OriginalAmount == 30m
                && account.NegativeBalance == 30m
                && account.PermanentSalesBlockedAt == f.Clock.Utc
                && decision.Actor == "BackgroundWorker"
                && decision.BlacklistedIdentities == 1,
            "write-off snapshots debt and locks selling without zeroing balances"
        );
        Check(
            await f.Db.SellerBlacklist.CountAsync(x =>
                !x.IsDemo && x.SourceSellerId == seller.SellerId
            ) == 1,
            "verified identity blacklisted in same decision"
        );
        Check(
            (await f.Db.FinancialTransactions.SingleAsync(x => x.Type == "DebtWriteOff")).Amount
                == 0m,
            "write-off audit entry moves no money"
        );
        await Reject<UnauthorizedAccessException>(() =>
            f.Service.RegisterAsync(Input("new-email@example.test", key: "new-email"))
        );
        await Reject<UnauthorizedAccessException>(() =>
            f.Service.ChangeIdentityAsync(
                seller.SellerId,
                new(new("CCCD", "999333333333"), 1, "bypass")
            )
        );
        await f.Finance.RepayDebtAsync(seller.SellerId, new(30m, 30m, 1, "clear-after-writeoff"));
        Check(
            account.NegativeBalance == 0
                && account.PermanentSalesBlockedAt != null
                && !account.DebtSuspended,
            "repaying clears debt but not permanent sales lock"
        );
        Check(
            await f.Db.SellerWriteOffRecoveries.SumAsync(x => x.Amount) == 30m
                && !await f.Db.SellerDebtEvents.AnyAsync(x => x.EventType == "DebtRestored"),
            "post-writeoff recovery recorded once without restoration notice"
        );
        await f.Finance.RepayDebtAsync(seller.SellerId, new(30m, 30m, 1, "clear-after-writeoff"));
        Check(
            await f.Db.SellerWriteOffRecoveries.CountAsync() == 1,
            "repayment retry cannot double-count write-off recovery"
        );
        await Reject<InvalidOperationException>(() =>
            f.Finance.ValidateNewSaleAsync(seller.SellerId)
        );
        await Reject<InvalidOperationException>(() =>
            f.Finance.RequestPayoutAsync(seller.SellerId, 1m, "new-payout", false)
        );
        Check(
            await new AuthenticationService(f.Db).ValidateCredentialsAsync(
                "new@example.test",
                "SellerDemo123!"
            ) != null,
            "permanent selling restriction keeps login available"
        );
        var buyer = new User { Role = "buyer", Email = "old-buyer@example.test" };
        f.Db.Users.Add(buyer);
        await f.Db.SaveChangesAsync();
        var order = new OrderTable
        {
            SellerId = seller.SellerId,
            BuyerId = buyer.Id,
            Status = "AwaitingPayment",
            TotalPrice = 100m,
            OrderDate = first.StartedAt!.Value,
            PaymentExpiresAt = DateTime.UtcNow.AddMinutes(15),
        };
        f.Db.OrderTables.Add(order);
        await f.Db.SaveChangesAsync();
        await new CheckoutService(f.Db, f.Finance, f.Config).PayCardAsync(
            order.Id,
            "4111111111111111",
            "12/30",
            "old-order"
        );
        Check(
            order.Status == "Paid" && account.PermanentSalesBlockedAt != null,
            "old payable order continues after permanent selling block"
        );
        decision.Reason = "tampered";
        await Reject<InvalidOperationException>(() => f.Db.SaveChangesAsync());
    }

    private static async Task LegacyGrace()
    {
        await using var f = new Fixture();
        await f.Init();
        f.Db.Users.Add(
            new User
            {
                Id = 50,
                Role = "seller",
                Email = "legacy@example.test",
            }
        );
        f.Db.SellerAccounts.Add(
            new SellerAccount
            {
                SellerId = 50,
                NegativeBalance = 25m,
                Status = "ManualRestricted",
                SalesMonth = f.Clock.Utc,
            }
        );
        await f.Db.SaveChangesAsync();
        await f.Finance.GetDebtAsync(50);
        var cycle = await f.Db.SellerDebtCycles.SingleAsync();
        cycle.WriteOffEligibleAt = null;
        cycle.LastRecoveryAt = null;
        await f.Db.SaveChangesAsync();
        f.Clock.Advance(1000);
        Check(
            await f.Finance.MaintainWriteOffsAsync() == 0,
            "legacy cycle gets full inactivity grace on first upgrade observation"
        );
        f.Clock.Advance(60);
        Check(
            await f.Finance.MaintainWriteOffsAsync() == 1,
            "legacy seller without identity can still be classified and blocked"
        );
        var decision = await f.Db.SellerDebtWriteOffs.SingleAsync();
        Check(
            decision.BlacklistedIdentities == 0
                && (await f.Db.SellerAccounts.SingleAsync()).Status == "ManualRestricted",
            "missing identity is explicit; other restrictions retained"
        );
    }

    private static async Task KeyAndEnvironment()
    {
        await using var f = new Fixture();
        await f.Init();
        await f.Service.SamplesAsync();
        var other = Configuration();
        other["SellerIdentity:HmacKey"] = Convert.ToBase64String(new byte[32]);
        var differentKey = new SellerRegistrationService(
            f.Db,
            new(other),
            other,
            new Host(),
            f.Clock
        );
        await Reject<InvalidOperationException>(() => differentKey.RegisterAsync(Input()));
        var production = new SellerRegistrationService(
            f.Db,
            f.Hasher,
            f.Config,
            new Host { EnvironmentName = "Production" },
            f.Clock
        );
        await Reject<UnauthorizedAccessException>(() => production.RegisterAsync(Input()));
        Check(
            !await f.Db.Users.AnyAsync(),
            "wrong key and non-development mock registration fail closed"
        );
    }

    internal sealed class Host : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "RegistrationTests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    internal sealed class Clock : TimeProvider
    {
        public DateTime Utc { get; private set; } = new(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);

        public override DateTimeOffset GetUtcNow() => new(Utc);

        public void Advance(int seconds) => Utc = Utc.AddSeconds(seconds);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db = new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        public IConfiguration Config = Configuration();
        public Clock Clock = new();
        public SellerIdentityHasher Hasher = null!;
        public SellerRegistrationService Service = null!;
        public SellerFinanceService Finance = null!;

        public async Task Init()
        {
            await Db.Database.EnsureCreatedAsync();
            Hasher = new(Config);
            Service = new(Db, Hasher, Config, new Host(), Clock);
            Finance = new(Db, Config, Clock);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
