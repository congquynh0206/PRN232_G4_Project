using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using G4.Application.Integrations;
using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Authentication;
using G4.Infrastructure.Checkout;
using G4.Infrastructure.Disputes;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Notifications;
using G4.Infrastructure.Persistence;
using G4.Infrastructure.Returns;
using G4.Infrastructure.Shipping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace G4.DemoData;

internal sealed class DemoFixture
{
    public const string Password = "G4@123456";
    public static readonly string HmacKey = Convert.ToBase64String(
        Enumerable.Range(1, 32).Select(x => (byte)x).ToArray()
    );
    private readonly ApplicationDbContext db;
    public readonly DemoClock Clock = new();
    public DateTime Anchor { get; private set; } = DateTime.UtcNow;
    public IConfiguration Config { get; }
    public List<DemoCase> Cases { get; } = [];
    public List<DemoLogin> Logins { get; } = [];
    public Dictionary<string, User> Sellers { get; } = [];
    public Dictionary<int, Product> Products { get; } = [];
    public User Buyer = null!,
        OtherBuyer = null!,
        HistoryBuyer = null!,
        Shipper = null!,
        OtherShipper = null!,
        Admin = null!;
    public SellerFinanceService Finance { get; }
    public ReturnService Returns { get; }
    public DisputeService Disputes { get; }
    public ShipmentService Shipping { get; }
    public CheckoutService Checkout { get; }
    private readonly DemoCarrier carrier = new();

    // Names, descriptions and image links copied from the Product table of CloneEbayDB
    // (products that have an image). Prices, weights and offer settings stay demo-specific
    // because the scenarios and DEMO_SCRIPT rely on them.
    private static readonly (string Title, string Description, string Image)[] Catalog =
    [
        (
            "Wireless Headphones",
            "Bluetooth over-ear headphones",
            "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcQ1zc86eb8oIu4eOwyIRNcCoPuxNAWf-W1EHw9y5Ro0ew&s=10"
        ),
        (
            "Compact Camera",
            "Travel-friendly digital camera",
            "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcRP7FbP95QRjKcY7JLuLoPMbeVpwWKcL3xA68zs6zySug&s=10"
        ),
        (
            "Smart Watch",
            "Fitness and notification watch",
            "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcTZM1VvAyaNjKUnWEHYeGxHreLLgWJxzMkXHnEMSOz-oQ&s=10"
        ),
        (
            "Phone Case",
            "Protective case for smartphone",
            "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcQl98_12c-T4RCsIOe6unigezKfg-XZ5iHyxrJ0Jmb4OA&s=10"
        ),
        (
            "Portable Charger",
            "Fast charging power bank",
            "https://m.media-amazon.com/images/I/61pCjlOUiZL.jpg"
        ),
        (
            "Wireless Mechanical Keyboard",
            "Compact bluetooth mechanical keyboard with long battery life",
            "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcShe7_jBwQTLsKBGu-6StC1sOERa9CUmbFaEHrCy5Cv3g&s=10"
        ),
        (
            "Wireless Gaming Mouse",
            "Low latency, ultra-lightweight gaming mouse",
            "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcRfbiVRSdaOTFiNlHnDYRUJkLp0iVtigTSOivefZW7IiQ&s=10"
        ),
        (
            "15.6-inch Portable Monitor",
            "Convenient Full HD IPS secondary screen",
            "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcQ9wS6i_nWkJWtxPtSzBbm55_UfWf3XMJQukmIEh4sszg&s=10"
        ),
        (
            "Mini Bluetooth Speaker",
            "IPX7 waterproof portable speaker",
            "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcRSUmmQH3f-8ycDpGoB3h3_5zVv8R6q78hj3KMh6eZ11A&s=10"
        ),
        (
            "Laptop Cooling Pad",
            "Dual-fan cooling pad for fast heat dissipation",
            "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcRQVVYJG5PXidY51s_tEMIGYyxLL7Zts_Ifx4GlDVylsFEac3B_1_fJMOs9&s=10"
        ),
        (
            "Bluetooth Speaker",
            "Portable Bluetooth speaker with rechargeable battery",
            "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcRbmgS-u5MMOo5vDBsFe37a60gDiVfek6dF1nEC2rk6jQ&s=10"
        ),
        (
            "Mechanical Keyboard",
            "Compact mechanical keyboard with backlit keys",
            "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcRbmgS-u5MMOo5vDBsFe37a60gDiVfek6dF1nEC2rk6jQ&s=10"
        ),
        (
            "Wireless Mouse",
            "Ergonomic wireless mouse with adjustable sensitivity",
            "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcQoPbdvCfDsXs7zQSraviFdeXQM_bjd4yqC8BlrP8KYbg&s=10"
        ),
        (
            "Screen Cleaning Kit",
            "Screen cleaning spray and microfiber cloth for electronic devices",
            "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcTizDIpLHo6h5atDwMJZUkpxaC79qegbKMcUAcDgTuJoQ&s=10"
        ),
        (
            "USB-C Hub",
            "Multiport USB-C adapter with HDMI and USB connections",
            "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcTJz0nwFYJfGV0Eizt75xP3YaQRLikf-P2JF82MqQFB0w&s=10"
        ),
    ];

    public DemoFixture(ApplicationDbContext db)
    {
        this.db = db;
        Clock.Value = Anchor;
        Config = new ConfigurationBuilder()
            .AddJsonFile(Path.GetFullPath("backend/G4.Api/appsettings.json"))
            .AddJsonFile(Path.GetFullPath("backend/G4.Api/appsettings.Development.json"))
            .AddInMemoryCollection(Profile())
            .Build();
        Finance = new SellerFinanceService(db, Config, Clock);
        Returns = new ReturnService(db, new DemoRefund(), carrier, Finance, Config, Clock);
        Disputes = new DisputeService(db, Finance, Returns, Config, Clock);
        Shipping = new ShipmentService(db, carrier, Config, Clock);
        Checkout = new CheckoutService(db, Finance, Config, clock: Clock);
    }

    // Demo-only timers. Prepared states stay valid for about 4 hours after the SQL runs.
    // Run-Demo.ps1 must use the same values.
    public const int PaymentHoldMinutes = 360;

    public static Dictionary<string, string?> Profile() =>
        new()
        {
            ["SellerIdentity:HmacKey"] = HmacKey,
            ["SellerIdentity:KeyId"] = "v1",
            ["Returns:WindowSeconds"] = "21600",
            ["Returns:ReceiptSeconds"] = "21600",
            ["Disputes:SellerResponseSeconds"] = "21600",
            ["Disputes:BuyerResponseSeconds"] = "21600",
            ["Disputes:ReturnReceiptSeconds"] = "21600",
            ["AddressChanges:ConfirmationSeconds"] = "21600",
            ["Finance:Levels:1:HoldSeconds"] = "14400",
            ["Finance:Levels:2:HoldSeconds"] = "7200",
            ["Finance:LevelPromotion:EvaluationSeconds"] = "86400",
            ["Finance:Debt:DueSeconds"] = "21600",
            ["Finance:Debt:WarningBeforeSeconds"] = "1800",
            ["Finance:WriteOff:AfterSeconds"] = "43200",
            ["Finance:WriteOff:InactivitySeconds"] = "1800",
            ["Checkout:PaymentHoldMinutes"] = PaymentHoldMinutes.ToString(
                CultureInfo.InvariantCulture
            ),
            ["Checkout:AutoExpireUnpaid"] = "false",
            ["Checkout:UnpaidStrikeLimit"] = "2",
            ["Checkout:UnpaidStrikeWindowMinutes"] = "43200",
            ["Mail:Mode"] = "Capture",
        };

    public async Task CreateAsync()
    {
        await AccountsAsync();
        await HistoryAsync("l2", 20, evaluate: true);
        await HistoryAsync("l3", 100, evaluate: true);
        await HistoryAsync("up2", 20, evaluate: false);
        await HistoryAsync("up3", 100, evaluate: false);
        // Keep historical transactions, but avoid a backlog of hundreds of synthetic emails.
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM dbo.UserNotification; DELETE FROM dbo.NotificationOutbox; DELETE FROM dbo.CommerceChange;"
        );
        Clock.Value = Anchor;
        Console.WriteLine("Seller levels and historical finance prepared.");
        await OrdersAsync();
        await ReturnCasesAsync();
        await DisputeCasesAsync();
        await AddressCasesAsync();
        await PromotionCasesAsync();
        await BestOfferCasesAsync();
        await BuyNowCasesAsync();
        await DebtCasesAsync();
        await EnsureLevel1WalletAsync();
        await PayoutCasesAsync();
        Clock.Value = Anchor;
        foreach (var account in await db.SellerAccounts.ToListAsync())
        {
            account.LastLevelEvaluatedAt = Anchor;
            account.SalesMonth = new DateTime(Anchor.Year, Anchor.Month, 1);
            account.MonthlySalesAmount = await (
                from p in db.Payments
                join o in db.OrderTables on p.OrderId equals o.Id
                where
                    o.SellerId == account.SellerId
                    && p.Status == "Succeeded"
                    && p.PaidAt >= account.SalesMonth
                    && p.PaidAt <= Anchor
                select o.SellerGrossSnapshot
            ).SumAsync();
        }
        await db.SaveChangesAsync();
        await new SellerRegistrationService(
            db,
            new SellerIdentityHasher(Config),
            Config,
            new DemoEnvironment(),
            Clock
        ).SamplesAsync();
        // Capture email locally via the production processor; no SMTP/network provider is used.
        var processor = new NotificationProcessor(
            new DemoDbFactory(db.Database.GetConnectionString()!),
            new NeverEmail(),
            Config,
            Clock
        );
        while (await processor.ProcessDueAsync(200, default) > 0) { }
        db.ChangeTracker.Clear();
        Console.WriteLine($"Fixture ready: {Cases.Count} scenarios.");
    }

    public async Task LoadReadyAsync(string guidePath)
    {
        Anchor = DateTime.SpecifyKind(
            (await db.OrderTables.SingleAsync(x => x.CheckoutKey == "demo-PAY-READY"))
                .OrderDate!
                .Value,
            DateTimeKind.Utc
        );
        Clock.Value = Anchor;
        Buyer = await db.Users.SingleAsync(x => x.Email == "buyer@demo.test");
        Admin = await db.Users.SingleAsync(x => x.Email == "admin@demo.test");
        Shipper = await db.Users.SingleAsync(x => x.Email == "shipper@demo.test");
        HistoryBuyer = await db.Users.SingleAsync(x => x.Email == "buyer.history@demo.test");
        var seller1 = await db.Users.SingleAsync(x => x.Email == "seller.l1@demo.test");
        Sellers["l1"] = seller1;
        Products[seller1.Id] = await db
            .Products.Where(x => x.SellerId == seller1.Id)
            .OrderBy(x => x.Id)
            .FirstAsync();
        foreach (var line in await File.ReadAllLinesAsync(guidePath))
        {
            var cells = line.Split('|').Select(x => x.Trim()).ToArray();
            if (cells.Length == 6 && cells[1].EndsWith("@demo.test"))
                Logins.Add(new(cells[1], cells[2], cells[3], cells[4] == "—" ? null : cells[4]));
            if (
                cells.Length == 8
                && System.Text.RegularExpressions.Regex.IsMatch(
                    cells[2],
                    "^(Order|Seller|Payout|Offer|Product) #[0-9]+$"
                )
            )
            {
                var parts = cells[2].Split(' ');
                Cases.Add(
                    new(
                        cells[1],
                        parts[0],
                        long.Parse(parts[1][1..]),
                        cells[3],
                        cells[4],
                        cells[5],
                        cells[6],
                        true
                    )
                );
            }
        }
        if (Cases.Count < 40 || Logins.Count != 16)
            throw new InvalidOperationException("Ready demo manifest is incomplete.");
        await EnsureLevel1WalletAsync();
    }

    private async Task EnsureLevel1WalletAsync()
    {
        var seller = Sellers["l1"];
        if (!await db.OrderTables.AnyAsync(x => x.CheckoutKey == "demo-HISTORY-WALLET-L1"))
        {
            Clock.Value = Anchor.AddDays(-7);
            var order = await OrderAsync(seller, "HISTORY-WALLET-L1", buyer: HistoryBuyer);
            await HistoricalDeliveredAsync(order, Clock.Value.AddDays(1));
            Clock.Value = Anchor;
            await Finance.ReleaseDueFundsAsync();
        }
        if (!Cases.Any(x => x.Code == "PAYOUT-L1-READY"))
            Case(
                "PAYOUT-L1-READY",
                "Seller",
                seller.Id,
                seller,
                "Seller cấp 1 tạo payout bằng Available từ đơn lịch sử đã mở khóa",
                "Cấp 1 cũng rút qua ngân hàng giả lập; không cần lên cấp 3"
            );
    }

    private async Task AccountsAsync()
    {
        Buyer = await UserAsync("buyer", "buyer", "Buyer trình diễn");
        OtherBuyer = await UserAsync(
            "buyer.other",
            "buyer",
            "Buyer có 1 điểm phạt Unpaid Item"
        );
        HistoryBuyer = await UserAsync("buyer.history", "buyer", "Buyer lịch sử hỗ trợ xét level");
        Shipper = await UserAsync("shipper", "shipper", "Shipper chính");
        OtherShipper = await UserAsync(
            "shipper.other",
            "shipper",
            "Shipper khác để kiểm tra quyền"
        );
        Admin = await UserAsync("admin", "admin", "Admin trình diễn");
        var names = new Dictionary<string, string>
        {
            ["l1"] = "Shop cấp 1 - đơn hàng/return/dispute",
            ["l2"] = "Shop cấp 2 - giữ tiền 15 phút",
            ["l3"] = "Shop cấp 3 - tiền vào ví ngay",
            ["up2"] = "Shop đủ tiêu chí lên cấp 2 - chờ bấm xét lại",
            ["up3"] = "Shop đủ tiêu chí lên cấp 3 - chờ bấm xét lại",
            ["debt"] = "Shop nợ chưa đến hạn - có thể trả nợ",
            ["overdue"] = "Shop nợ quá hạn - bị hạn chế",
            ["writeoff"] = "Shop đã write-off - khóa bán vĩnh viễn",
            ["recovered"] = "Shop đã trả hết sau write-off - vẫn khóa bán",
            ["limit"] = "Shop cấp 1 gần chạm hạn mức tháng",
        };
        var registry = new SellerRegistrationService(
            db,
            new SellerIdentityHasher(Config),
            Config,
            new DemoEnvironment(),
            Clock
        );
        var index = 0;
        foreach (var (code, name) in names)
        {
            Clock.Value = Anchor.AddDays(-120);
            var identity = "999100" + (++index).ToString("D6", CultureInfo.InvariantCulture);
            var result = await registry.RegisterAsync(
                new(
                    name,
                    $"seller.{code}@demo.test",
                    Password,
                    Password,
                    name,
                    new("CCCD", identity),
                    "demo-register-" + code
                )
            );
            var seller = await db.Users.SingleAsync(x => x.Id == result.SellerId);
            Sellers[code] = seller;
            Logins.Add(new(seller.Email!, "seller", name, identity));
            db.Addresses.Add(
                new Address
                {
                    UserId = seller.Id,
                    FullName = name,
                    Phone = $"+849000001{index:00}",
                    Street = "10 Phố Demo",
                    City = "Phường Hoàn Kiếm",
                    State = "Hà Nội",
                    Country = "Vietnam",
                    IsDefault = true,
                }
            );
            for (var p = 0; p < 3; p++)
            {
                var item = Catalog[((index - 1) * 3 + p) % Catalog.Length];
                var product = new Product
                {
                    SellerId = seller.Id,
                    Title = $"[{code.ToUpperInvariant()}] {item.Title}",
                    Description = item.Description,
                    Images = item.Image,
                    Price = 100 + p * 20,
                    WeightKg = 1.2m + p * .3m,
                    IsAuction = false,
                };
                db.Products.Add(product);
                await db.SaveChangesAsync();
                db.Inventories.Add(new Inventory { ProductId = product.Id, Quantity = 1000 });
                if (p == 0)
                    Products[seller.Id] = product;
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        Clock.Value = Anchor;
        var address = new Address
        {
            UserId = Buyer.Id,
            FullName = "Buyer Demo",
            Phone = "+84900000011",
            Street = "20 Phố Demo",
            City = "Phường Hoàn Kiếm",
            State = "Hà Nội",
            Country = "Vietnam",
            IsDefault = true,
        };
        db.Addresses.Add(address);
        db.Addresses.Add(
            new Address
            {
                UserId = Buyer.Id,
                FullName = "Buyer Demo",
                Phone = "+84900000011",
                Street = "30 Đường Demo",
                City = "Phường Bến Thành",
                State = "Hồ Chí Minh",
                Country = "Vietnam",
                PostalCode = "70000",
            }
        );
        // US address to demo the +1 dial code, required ZIP and the VN ↔ US zone.
        db.Addresses.Add(
            new Address
            {
                UserId = Buyer.Id,
                FullName = "Buyer Demo",
                Phone = "+12125550123",
                Street = "350 5th Ave",
                City = "New York",
                State = "NY",
                Country = "United States",
                PostalCode = "10118",
            }
        );
        db.Addresses.Add(
            new Address
            {
                UserId = OtherBuyer.Id,
                FullName = "Buyer khác",
                Phone = "+84900000012",
                Street = "40 Phố Demo",
                City = "Phường Ba Đình",
                State = "Hà Nội",
                Country = "Vietnam",
                IsDefault = true,
            }
        );
        db.Addresses.Add(
            new Address
            {
                UserId = HistoryBuyer.Id,
                FullName = "Buyer lịch sử",
                Phone = "+84900000013",
                Street = "50 Phố Demo",
                City = "Phường Cửa Nam",
                State = "Hà Nội",
                Country = "Vietnam",
                IsDefault = true,
            }
        );
        await db.SaveChangesAsync();
    }

    private async Task<User> UserAsync(string name, string role, string description)
    {
        var salt = SHA256.HashData(Encoding.UTF8.GetBytes("g4-demo-public-salt-" + name))[..16];
        var hash = Rfc2898DeriveBytes.Pbkdf2(Password, salt, 210000, HashAlgorithmName.SHA256, 32);
        var user = new User
        {
            Username = description,
            Email = name + "@demo.test",
            Role = role,
            Password =
                $"PBKDF2$210000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        Logins.Add(new(user.Email, role, description, null));
        return user;
    }

    private async Task HistoryAsync(string code, int count, bool evaluate)
    {
        var seller = Sellers[code];
        for (var i = 0; i < count; i++)
        {
            Clock.Value = Anchor.AddDays(-50).AddHours(i * 8);
            var order = await OrderAsync(
                seller,
                $"HISTORY-{code}-{i + 1}",
                paid: true,
                goods: 10,
                buyer: HistoryBuyer
            );
            await HistoricalDeliveredAsync(order, Clock.Value.AddDays(1));
            db.SellerOrderFeedbacks.Add(
                new SellerOrderFeedback
                {
                    OrderId = order.Id,
                    SellerId = seller.Id,
                    BuyerId = HistoryBuyer.Id,
                    Rating = 5,
                    Comment = "Lịch sử demo: giao đúng hạn.",
                    RequestKey = "demo-feedback-" + order.Id,
                    CreatedAt = Clock.Value.AddDays(1).AddHours(1),
                }
            );
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            if ((i + 1) % 25 == 0)
                Console.WriteLine($"Historical orders {code}: {i + 1}/{count}");
        }
        Clock.Value = Anchor;
        await Finance.ReleaseDueFundsAsync();
        if (evaluate)
            await Finance.EvaluateLevelAsync(seller.Id);
        Case(
            "LEVEL-" + code.ToUpperInvariant(),
            "Seller",
            seller.Id,
            seller,
            evaluate ? "Xem level, số liệu và lịch sử xét cấp" : "Bấm Xét lại cấp để chạy xét thật",
            evaluate
                ? $"Cấp {(code == "l3" ? 3 : 2)} có dữ liệu đơn/feedback/tracking hỗ trợ"
                : $"Đủ tiêu chí lên {(code == "up3" ? 3 : 2)}; chưa tự nâng trước lúc demo"
        );
    }

    private static string Text(Address a) =>
        string.Join(", ", a.FullName, a.Street, a.City, a.State, a.Country);

    public async Task<OrderTable> OrderAsync(
        User seller,
        string code,
        bool paid = true,
        decimal goods = 100,
        User? buyer = null
    )
    {
        buyer ??= Buyer;
        var address = await db.Addresses.FirstAsync(x =>
            x.UserId == buyer.Id && x.IsDefault == true
        );
        var pickup = await db.Addresses.FirstAsync(x =>
            x.UserId == seller.Id && x.IsDefault == true
        );
        var product = Products[seller.Id];
        var shipping = 2.10m;
        var order = new OrderTable
        {
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            AddressId = address.Id,
            OrderDate = Clock.Value,
            UpdatedAt = Clock.Value,
            Status = paid ? "Paid" : "AwaitingPayment",
            PaymentExpiresAt = Clock.Value.AddMinutes(PaymentHoldMinutes),
            Subtotal = goods,
            ShippingFee = shipping,
            TotalPrice = goods + shipping,
            TotalWeightKg = 1.2m,
            Currency = "USD",
            CheckoutKey = "demo-" + code,
            // Same snapshot shape as CheckoutService (Hà Nội → Hà Nội: 2,10 USD for 1,2 kg).
            AddressSnapshot = Text(address),
            PickupAddressSnapshot = Text(pickup),
            DeliveryNameSnapshot = address.FullName,
            DeliveryStreetSnapshot = address.Street,
            DeliveryCitySnapshot = address.City,
            DeliveryPostalCodeSnapshot = address.PostalCode,
            DeliveryPhoneSnapshot = address.Phone,
            DeliveryStateSnapshot = address.State,
            DeliveryCountrySnapshot = address.Country,
            PickupStateSnapshot = pickup.State,
            PickupCountrySnapshot = pickup.Country,
            PricingSchemaVersion = 1,
            ShippingBase = shipping,
            SellerGrossSnapshot = goods + shipping,
        };
        db.OrderTables.Add(order);
        await db.SaveChangesAsync();
        db.OrderItems.Add(
            new OrderItem
            {
                OrderId = order.Id,
                ProductId = product.Id,
                Quantity = 1,
                UnitPrice = goods,
                UnitWeightKgSnapshot = 1.2m,
                SellerIdSnapshot = seller.Id,
                ProductTitleSnapshot = $"[{code}] {product.Title}",
            }
        );
        (await db.Inventories.SingleAsync(x => x.ProductId == product.Id)).Quantity--;
        await db.SaveChangesAsync();
        if (paid)
        {
            var payment = new Payment
            {
                OrderId = order.Id,
                UserId = buyer.Id,
                Amount = order.TotalPrice,
                Method = "Card",
                Status = "Succeeded",
                PaidAt = Clock.Value,
                CreatedAt = Clock.Value,
                UpdatedAt = Clock.Value,
                IdempotencyKey = "demo-pay-" + code,
                ProviderTransactionId = "CARD-DEMO-" + order.Id,
            };
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
            await Finance.RecordSuccessfulPaymentAsync(order.Id, payment.Id);
        }
        return order;
    }

    private async Task<OrderTable> OverdueOrderAsync(User seller, string code, User buyer)
    {
        var order = await OrderAsync(seller, code, paid: false, buyer: buyer);
        var created = Clock.Value.AddMinutes(-PaymentHoldMinutes - 60);
        order.OrderDate = created;
        order.UpdatedAt = created;
        order.PaymentExpiresAt = Clock.Value.AddHours(-1);
        await db.SaveChangesAsync();
        return order;
    }

    private async Task HistoricalDeliveredAsync(OrderTable order, DateTime delivered)
    {
        var created = order.OrderDate!.Value;
        var shipment = new ShippingInfo
        {
            OrderId = order.Id,
            Direction = "Outbound",
            Status = "Delivered",
            Carrier = "G4 Carrier Simulator",
            TrackingNumber = "G4-" + order.Id + "-O",
            IdempotencyKey = $"ship-{order.Id}-outbound",
            CreatedAt = created,
            ShipperId = Shipper.Id,
            ClaimedAt = created.AddMinutes(10),
            DeliveryAttempts = 1,
            DeliveredAt = delivered,
            EstimatedArrival = delivered.AddHours(2),
            PickupAddressSnapshot = order.PickupAddressSnapshot,
            DeliveryAddressSnapshot = order.AddressSnapshot,
        };
        db.ShippingInfos.Add(shipment);
        await db.SaveChangesAsync();
        var states = new[]
        {
            "LabelCreated",
            "PickedUp",
            "InTransit",
            "OutForDelivery",
            "Delivered",
        };
        for (var i = 0; i < states.Length; i++)
            db.ShippingEvents.Add(
                new ShippingEvent
                {
                    ShippingInfoId = shipment.Id,
                    ExternalEventId = $"demo-history-{order.Id}-{i}",
                    Status = states[i],
                    OccurredAt = i == 4 ? delivered : created.AddHours(i),
                    ReceivedAt = i == 4 ? delivered : created.AddHours(i),
                    ActorUserId = i == 0 ? null : Shipper.Id,
                    ActorRole = i == 0 ? "system" : "shipper",
                    Location = "Hà Nội",
                    Note = "Sự kiện dữ liệu demo lịch sử",
                }
            );
        ReturnWindowPolicy.SnapshotDelivery(order, delivered, Config);
        order.Status = "Closed";
        order.UpdatedAt = delivered.AddHours(3);
        await db.SaveChangesAsync();
        await Finance.StartDeliveryHoldAsync(order.Id);
    }

    public void Case(
        string code,
        string type,
        long id,
        User seller,
        string steps,
        string expected,
        string role = "seller",
        bool timed = false
    ) => Cases.Add(new(code, type, id, seller.Email!, role, steps, expected, timed));

    public async Task<ShippingInfo> ToShippingAsync(OrderTable order, string status = "Delivered")
    {
        var shipment = await Shipping.CreateOutboundAsync(order.Id);
        shipment.CreatedAt = Clock.Value;
        await db.SaveChangesAsync();
        if (status == "LabelCreated")
            return shipment;
        await Shipping.ClaimAsync(shipment.Id, Shipper.Id);
        foreach (var state in new[] { "PickedUp", "InTransit", "OutForDelivery", "Delivered" })
        {
            await Shipping.RecordEventAsync(
                shipment.Id,
                $"demo-event-{shipment.Id}-{state}",
                state,
                "Hà Nội",
                "Mốc chuẩn bị demo",
                shipperId: Shipper.Id
            );
            if (state == status)
                break;
        }
        return shipment;
    }

    public async Task OrdersAsync()
    {
        var seller = Sellers["l1"];
        var unpaid = await OrderAsync(seller, "PAY-READY", paid: false);
        Case(
            "PAY-READY",
            "Order",
            unpaid.Id,
            seller,
            "Buyer mở đơn, thanh toán thẻ hoặc PayPal Sandbox đã cấu hình",
            "Đơn chờ thanh toán; hạn 6 giờ sau khi chạy SQL",
            "buyer",
            true
        );
        var failed = await OrderAsync(seller, "PAY-FAILED", paid: false);
        db.Payments.Add(
            new Payment
            {
                OrderId = failed.Id,
                UserId = Buyer.Id,
                Method = "Card",
                Status = "Failed",
                Amount = failed.TotalPrice,
                CreatedAt = Anchor,
                UpdatedAt = Anchor,
                ErrorCode = "Declined",
                IdempotencyKey = "demo-failed-card",
            }
        );
        await db.SaveChangesAsync();
        Case(
            "PAY-FAILED",
            "Order",
            failed.Id,
            seller,
            "Buyer xem lần thanh toán bị từ chối rồi thử thẻ mới",
            "Có lịch sử Failed; vẫn chờ thanh toán trong thời hạn",
            "buyer",
            true
        );
        // Unpaid Item: the worker never cancels; the seller cancels after the deadline.
        var overdue = await OverdueOrderAsync(seller, "UNPAID-OVERDUE", Buyer);
        Case(
            "UNPAID-OVERDUE",
            "Order",
            overdue.Id,
            seller,
            "Seller mở đơn quá hạn thanh toán, bấm Hủy đơn quá hạn thanh toán",
            "Cancelled lý do UnpaidItem, hoàn kho một lần; buyer bị 1 điểm phạt (chưa bị khóa)"
        );
        Clock.Value = Anchor.AddDays(-2);
        var earlier = await OverdueOrderAsync(seller, "UNPAID-STRIKE-1", OtherBuyer);
        await Checkout.SellerCancelUnpaidAsync(
            earlier.Id,
            seller.Id,
            "Dữ liệu demo: lần không thanh toán trước đó."
        );
        Clock.Value = Anchor;
        var ban = await OverdueOrderAsync(seller, "UNPAID-BAN", OtherBuyer);
        Case(
            "UNPAID-BAN",
            "Order",
            ban.Id,
            seller,
            "Seller hủy đơn quá hạn của buyer.other; sau đó đăng nhập buyer.other@demo.test",
            "Điểm phạt thứ 2 trong 30 ngày: tài khoản buyer.other bị khóa, đăng nhập bị từ chối"
        );
        var cancel = await OrderAsync(seller, "CANCEL-UNPAID", paid: false);
        await Checkout.CancelUnpaidAsync(cancel.Id);
        Case(
            "CANCEL-UNPAID",
            "Order",
            cancel.Id,
            seller,
            "Buyer xem đơn đã hủy khi chưa thanh toán",
            "Cancelled, không có refund",
            "buyer"
        );
        var paid = await OrderAsync(seller, "PAID-READY");
        Case(
            "PAID-READY",
            "Order",
            paid.Id,
            seller,
            "Seller chuẩn bị hàng/tạo vận đơn",
            "Có payment và settlement, chưa có vận đơn"
        );
        var requestCancel = await OrderAsync(seller, "CANCEL-REQUEST");
        await Returns.RequestCancelAsync(requestCancel.Id);
        Case(
            "CANCEL-REQUEST",
            "Order",
            requestCancel.Id,
            seller,
            "Seller duyệt hoặc từ chối hủy",
            "CancelRequested; duyệt thì refund, từ chối giữ đơn"
        );
        foreach (var state in new[] { "LabelCreated", "PickedUp", "InTransit", "OutForDelivery" })
        {
            var o = await OrderAsync(seller, "SHIP-" + state.ToUpperInvariant());
            var s = await ToShippingAsync(o, state);
            Case(
                "SHIP-" + state.ToUpperInvariant(),
                "Order",
                o.Id,
                seller,
                state == "LabelCreated"
                    ? "Shipper nhận vận đơn đang chờ"
                    : "Shipper chính cập nhật bước tiếp theo",
                $"Vận đơn {s.Id}, trạng thái {state}",
                "shipper"
            );
        }
        foreach (var scheduled in new[] { false, true })
        {
            var code = scheduled ? "SHIP-REATTEMPT" : "SHIP-FAILED";
            var o = await OrderAsync(seller, code);
            var s = await ToShippingAsync(o, "OutForDelivery");
            await Shipping.RecordEventAsync(
                s.Id,
                "demo-failed-" + s.Id,
                "DeliveryFailed",
                "Hà Nội",
                "Buyer vắng nhà",
                shipperId: Shipper.Id
            );
            if (scheduled)
                await Shipping.RecordEventAsync(
                    s.Id,
                    "demo-schedule-" + s.Id,
                    "ReattemptScheduled",
                    "Hà Nội",
                    "Giao lại theo hẹn",
                    shipperId: Shipper.Id,
                    reattemptAt: new DateTimeOffset(Anchor.AddMinutes(30))
                );
            Case(
                code,
                "Order",
                o.Id,
                seller,
                scheduled ? "Shipper bấm bắt đầu giao lại" : "Shipper lên lịch giao lại",
                "Lên lịch không tăng lượt; bắt đầu giao mới tăng lượt",
                "shipper"
            );
        }
        var rts = await OrderAsync(seller, "SHIP-RETURNING");
        var srts = await ToShippingAsync(rts, "OutForDelivery");
        await Shipping.RecordEventAsync(
            srts.Id,
            "demo-rts-fail",
            "DeliveryFailed",
            "Hà Nội",
            "Không liên lạc được",
            shipperId: Shipper.Id
        );
        await Shipping.RecordEventAsync(
            srts.Id,
            "demo-rts",
            "ReturningToSender",
            "Hà Nội",
            "Chuyển hoàn thực tế",
            shipperId: Shipper.Id
        );
        Case(
            "SHIP-RETURNING",
            "Order",
            rts.Id,
            seller,
            "Shipper ghi ReturnedToSeller khi hàng thực sự về",
            "Chiều đi chuyển hoàn, khác buyer trả hàng",
            "shipper"
        );
        var delivered = await OrderAsync(seller, "DELIVERED-READY");
        await ToShippingAsync(delivered);
        Case(
            "DELIVERED-READY",
            "Order",
            delivered.Id,
            seller,
            "Buyer mở yêu cầu trả hàng/đánh giá; Seller xem tiền giữ",
            "Delivered, cửa sổ return 6 giờ theo profile demo",
            "buyer",
            true
        );
        var instant = await OrderAsync(Sellers["l3"], "LEVEL3-INSTANT");
        Case(
            "LEVEL3-INSTANT",
            "Order",
            instant.Id,
            Sellers["l3"],
            "Seller cấp 3 mở ví và chi tiết đơn chưa giao",
            "Net vào Available ngay sau payment; chưa giao vẫn không phải Processing"
        );
        var l2 = await OrderAsync(Sellers["l2"], "LEVEL2-HOLD");
        await ToShippingAsync(l2);
        Case(
            "LEVEL2-HOLD",
            "Order",
            l2.Id,
            Sellers["l2"],
            "Seller cấp 2 xem lịch giữ tiền",
            "Processing; hold snapshot 2 giờ từ Delivered",
            timed: true
        );
        var limit = await OrderAsync(Sellers["limit"], "MONTH-LIMIT", goods: 4975);
        Case(
            "MONTH-LIMIT",
            "Order",
            limit.Id,
            Sellers["limit"],
            "Buyer tạo/thanh toán thêm đơn trên 22,90 USD cho shop này",
            "Doanh số 4977,10 USD; hạn mức mặc định cấp 1 là 5000 USD/tháng UTC",
            "buyer"
        );
    }

    private async Task<ReturnRequest> ReturnAsync(string code, string reason = "ChangedMind")
    {
        var order = await OrderAsync(Sellers["l1"], code);
        await ToShippingAsync(order);
        var quote = await Returns.QuoteAsync(order.Id, reason);
        return await Returns.RequestPricedReturnAsync(
            order.Id,
            new(
                reason,
                "Yêu cầu chuẩn bị cho demo " + code,
                "demo-return-" + code,
                quote.PricingFingerprint
            )
        );
    }

    private async Task ReturnCasesAsync()
    {
        foreach (var reason in new[] { "ChangedMind", "NotAsDescribed" })
        {
            var r = await ReturnAsync("RETURN-REQUEST-" + reason.ToUpperInvariant(), reason);
            Case(
                "RETURN-REQUEST-" + reason.ToUpperInvariant(),
                "Order",
                r.OrderId!.Value,
                Sellers["l1"],
                "Seller xem báo giá và duyệt hoặc từ chối return",
                "Requested; phí/tiền hoàn đã snapshot theo lý do"
            );
        }
        var rejected = await ReturnAsync("RETURN-REJECTED");
        await Returns.RejectAsync(
            rejected.Id,
            "Shop chưa đồng ý lý do; buyer có thể mở dispute theo chức năng hiện có."
        );
        Case(
            "RETURN-REJECTED",
            "Order",
            rejected.OrderId!.Value,
            Sellers["l1"],
            "Buyer xem bị từ chối và mở tranh chấp",
            "Không tự refund khi bị từ chối; RT2 deadline riêng chưa triển khai",
            "buyer"
        );
        var shipping = await ReturnAsync("RETURN-SHIPPING");
        await Returns.ApproveAsync(shipping.Id);
        Case(
            "RETURN-SHIPPING",
            "Order",
            shipping.OrderId!.Value,
            Sellers["l1"],
            "Shipper nhận vận đơn chiều Return và giao về seller",
            "Hai chiều có tracking riêng; chưa refund",
            "shipper"
        );
        var receive = await ReturnAsync("RETURN-RECEIVE");
        await Returns.ApproveAsync(receive.Id);
        await DeliverReturnAsync(receive);
        Case(
            "RETURN-RECEIVE",
            "Order",
            receive.OrderId!.Value,
            Sellers["l1"],
            "Seller xác nhận nhận hàng hợp lệ hoặc báo vấn đề",
            "Return Delivered; chờ kiểm tra, receipt 6 giờ theo profile",
            timed: true
        );
        var refunded = await ReturnAsync("RETURN-REFUNDED");
        await Returns.ApproveAsync(refunded.Id);
        await DeliverReturnAsync(refunded);
        await Returns.MarkReceivedAsync(refunded.Id);
        Case(
            "RETURN-REFUNDED",
            "Order",
            refunded.OrderId!.Value,
            Sellers["l1"],
            "Buyer/Seller/Admin xem kết quả tiền hoàn và ledger",
            "Refund Card mô phỏng thành công; không gọi PayPal",
            "buyer"
        );
        var feedback = new ProductFeedbackService(db, Clock);
        var reviewOrder = await OrderAsync(Sellers["l1"], "REVIEW-READY");
        await ToShippingAsync(reviewOrder);
        await feedback.SubmitAsync(
            Buyer.Id,
            reviewOrder.Id,
            Products[Sellers["l1"].Id].Id,
            new(5, "Sản phẩm demo tốt, đóng gói cẩn thận.", "demo-product-review")
        );
        Case(
            "REVIEW-READY",
            "Order",
            reviewOrder.Id,
            Sellers["l1"],
            "Seller vào Đánh giá sản phẩm và trả lời review",
            "Review sản phẩm có verified purchase, chưa có reply"
        );
    }

    private async Task DeliverReturnAsync(ReturnRequest r)
    {
        var s = await db.ShippingInfos.SingleAsync(x =>
            x.OrderId == r.OrderId && x.Direction == "Return"
        );
        await Shipping.ClaimAsync(s.Id, Shipper.Id);
        foreach (var state in new[] { "PickedUp", "InTransit", "OutForDelivery", "Delivered" })
            await Shipping.RecordEventAsync(
                s.Id,
                $"demo-return-event-{s.Id}-{state}",
                state,
                "Hà Nội",
                "Tracking chiều trả",
                shipperId: Shipper.Id
            );
    }

    private async Task DisputeCasesAsync()
    {
        foreach (var kind in new[] { "SELLER", "BUYER", "ADMIN", "RESOLVED" })
        {
            var o = await OrderAsync(Sellers["l1"], "DISPUTE-" + kind);
            await ToShippingAsync(o);
            var dispute = await Disputes.OpenAsync(
                o.Id,
                Buyer.Id,
                new("Sản phẩm demo không đúng mô tả.", ["https://example.com/demo/buyer-evidence"])
            );
            if (kind != "SELLER")
                await Disputes.ProposeAsync(
                    dispute.Id,
                    Sellers["l1"].Id,
                    new(
                        "KeepOrder",
                        "Shop đề nghị giữ đơn, đã cung cấp thông tin mô phỏng.",
                        ["https://example.com/demo/seller-evidence"]
                    )
                );
            if (kind is "ADMIN" or "RESOLVED")
                await Disputes.RespondAsync(
                    dispute.Id,
                    Buyer.Id,
                    new(false, "Buyer không đồng ý, nhờ admin xem xét.")
                );
            if (kind == "RESOLVED")
            {
                var quote = await Disputes.PreviewDecisionAsync(
                    dispute.Id,
                    Admin.Id,
                    new("PartialRefund", "Seller", 20)
                );
                var entry = await db
                    .DisputeEntries.Where(x => x.DisputeId == dispute.Id)
                    .Select(x => x.Id)
                    .ToArrayAsync();
                await Disputes.ResolveDecisionAsync(
                    dispute.Id,
                    Admin.Id,
                    new(
                        "PartialRefund",
                        "Seller",
                        20,
                        "Hoàn một phần 20 USD theo dữ liệu demo.",
                        entry,
                        "demo-decision",
                        quote.PricingFingerprint
                    )
                );
            }
            Case(
                "DISPUTE-" + kind,
                "Order",
                o.Id,
                Sellers["l1"],
                kind switch
                {
                    "SELLER" => "Seller gửi phương án",
                    "BUYER" => "Buyer chấp nhận hoặc chuyển admin",
                    "ADMIN" => "Admin preview và ra phán quyết",
                    _ => "Admin/Buyer xem quyết định và lịch sử hoàn một phần",
                },
                "Dispute có lịch sử và khoản hold thực qua service",
                kind == "ADMIN" ? "admin"
                    : kind == "BUYER" ? "buyer"
                    : "seller",
                kind is "SELLER" or "BUYER"
            );
        }
    }

    private async Task AddressCasesAsync()
    {
        var service = new AddressChangeService(db, Finance, Config, Clock);
        foreach (var assigned in new[] { false, true })
        {
            var code = assigned ? "ADDRESS-SHIPPER" : "ADDRESS-SELLER";
            var order = await OrderAsync(Sellers["l1"], code);
            if (assigned)
            {
                var shipment = await Shipping.CreateOutboundAsync(order.Id);
                await Shipping.ClaimAsync(shipment.Id, Shipper.Id);
            }
            var input = new AddressChangeInput(
                new(
                    "Buyer Demo",
                    "21 Phố Demo",
                    "Phường Hoàn Kiếm",
                    "Hà Nội",
                    "Vietnam",
                    Phone: "0912345678"
                ),
                "Đổi địa chỉ và số điện thoại trong Hà Nội để demo quy trình xác nhận.",
                "demo-address-" + code
            );
            var quote = await service.QuoteAsync(order.Id, Buyer.Id, input);
            var request = await service.RequestAsync(
                order.Id,
                Buyer.Id,
                input with
                {
                    QuoteToken = quote.Token,
                }
            );
            if (assigned)
                await service.DecideAsync(
                    request.Id,
                    Sellers["l1"].Id,
                    new(true, "Seller đồng ý, chờ shipper.")
                );
            Case(
                code,
                "Order",
                order.Id,
                Sellers["l1"],
                assigned
                    ? "Shipper quyết định địa chỉ sau seller"
                    : "Seller duyệt, chưa có shipper nên áp dụng ngay",
                assigned
                    ? "AwaitingShipper; địa chỉ cũ còn hiệu lực"
                    : "Pending; mới áp dụng khi đủ xác nhận",
                assigned ? "shipper" : "seller",
                true
            );
        }
    }

    private async Task PromotionCasesAsync()
    {
        var seller = Sellers["l1"];
        foreach (
            var item in new[]
            {
                ("Coupon", "DEMO10", 10m, false),
                ("Shipping", "", 0m, true),
                ("Sale", "", 5m, false),
                ("Volume", "", 10m, false),
                ("Order", "", 8m, false),
            }
        )
        {
            var p = new Promotion
            {
                Name = "Demo " + item.Item1,
                Type = item.Item1,
                FundingSource = "Seller",
                SellerId = seller.Id,
                CreatedById = seller.Id,
                Code = item.Item2.Length == 0 ? null : item.Item2,
                Value = item.Item3,
                IsPercent = item.Item1 != "Coupon",
                FreeShipping = item.Item4,
                StartAt = Anchor.AddDays(-1),
                EndAt = Anchor.AddDays(7),
                CreatedAt = Anchor,
                UpdatedAt = Anchor,
                MinQuantity = item.Item1 == "Order" ? 2 : 1,
                MaxUsage = 100,
                MaxUsagePerBuyer = 10,
                Budget = 1000,
                IsPublicCoupon = item.Item2.Length > 0,
            };
            if (item.Item1 == "Volume")
                p.Tiers.Add(new PromotionTier { MinQuantity = 2, Percent = 10 });
            db.Promotions.Add(p);
        }
        db.Promotions.Add(
            new Promotion
            {
                Name = "Coupon sàn tài trợ 5 USD",
                Type = "Coupon",
                FundingSource = "Platform",
                CreatedById = Admin.Id,
                Code = "SAN5",
                Value = 5,
                StartAt = Anchor.AddDays(-1),
                EndAt = Anchor.AddDays(7),
                CreatedAt = Anchor,
                UpdatedAt = Anchor,
                Budget = 1000,
                MaxUsage = 100,
                MaxUsagePerBuyer = 10,
                IsPublicCoupon = true,
            }
        );
        await db.SaveChangesAsync();
        Case(
            "PROMOTIONS",
            "Seller",
            seller.Id,
            seller,
            "Seller xem promotion; Buyer random giỏ và nhập DEMO10 hoặc SAN5 theo shop",
            "Sale, volume/order, free shipping, coupon seller và coupon sàn; DEMO10/SAN5 hiện ở tab "
                + "Ưu đãi của buyer; không có free returns chưa triển khai",
            "buyer"
        );
    }

    private async Task BestOfferCasesAsync()
    {
        var seller = Sellers["l1"];
        var service = new BestOfferService(db, Finance, Clock, Config);
        var products = await db
            .Products.Where(x => x.SellerId == seller.Id)
            .OrderBy(x => x.Id)
            .ToListAsync();
        var address = await db.Addresses.FirstAsync(x =>
            x.UserId == Buyer.Id && x.IsDefault == true
        );
        Clock.Value = Anchor.AddMinutes(-20);
        // Product 1 (100 USD): auto-decline below 50, auto-accept from 95.
        await service.UpdateProductSettingsAsync(
            seller.Id,
            new(products[0].Id, true, 95, 50),
            default
        );
        await service.UpdateProductSettingsAsync(
            seller.Id,
            new(products[1].Id, true, null, null),
            default
        );
        await service.UpdateProductSettingsAsync(
            seller.Id,
            new(products[2].Id, true, null, null),
            default
        );

        Task<BestOfferView> Offer(Product product, decimal amount, string code) =>
            service.CreateAsync(
                Buyer.Id,
                new(product.Id, amount, address.Id, "PayLater", null, null, "demo-bo-" + code),
                default
            );

        var accepted = await Offer(products[0], 85, "accepted");
        accepted = await service.SellerAcceptAsync(
            seller.Id,
            accepted.Id,
            new(Key: "demo-bo-accepted-seller"),
            default
        );
        Case(
            "BO-ACCEPTED",
            "Order",
            accepted.OrderId!.Value,
            seller,
            "Buyer mở Đơn hàng và thanh toán đơn tạo từ Best Offer (85 USD thay vì 100 USD)",
            "Đơn AwaitingPayment theo giá đã chốt; còn 4/5 lượt trả giá cho sản phẩm này",
            "buyer"
        );
        Case(
            "BO-AUTO",
            "Product",
            products[0].Id,
            seller,
            "Buyer vào Trả giá, gửi 40 USD rồi 96 USD cho sản phẩm 1",
            "Dưới 50 USD tự động bị từ chối; từ 95 USD tự động chấp nhận và tạo đơn",
            "buyer"
        );
        var pendingSeller = await Offer(products[1], 100, "pending-seller");
        Case(
            "BO-PENDING-SELLER",
            "Offer",
            pendingSeller.Id,
            seller,
            "Seller vào Best Offer: chấp nhận (nhập mật khẩu), từ chối hoặc trả giá lại (100–120 USD)",
            "Buyer đề nghị 100 USD cho sản phẩm 120 USD; chờ seller phản hồi trong 24 giờ"
        );
        var pendingBuyer = await Offer(products[2], 110, "pending-buyer");
        await service.SellerCounterAsync(
            seller.Id,
            pendingBuyer.Id,
            new(Amount: 130, Key: "demo-bo-pending-buyer-counter"),
            default
        );
        Case(
            "BO-PENDING-BUYER",
            "Offer",
            pendingBuyer.Id,
            seller,
            "Buyer vào Trả giá: chấp nhận 130 USD (nhập mật khẩu), từ chối hoặc trả giá lại",
            "Seller đã counter 130 USD cho đề nghị 110 USD; chấp nhận thì tạo đơn chờ thanh toán",
            "buyer"
        );
        Clock.Value = Anchor;
    }

    private async Task BuyNowCasesAsync()
    {
        var seller = Sellers["l1"];
        foreach (var (item, label, stock, price) in new[]
        {
            (Catalog[1], "chỉ còn 1 cái", 1, 150m),
            (Catalog[4], "trả ngay", 50, 80m),
        })
        {
            var product = new Product
            {
                SellerId = seller.Id,
                Title = $"[L1] Buy It Now - {item.Title} ({label})",
                Description =
                    $"{item.Description}. Bắt buộc thanh toán ngay (Immediate Payment).",
                Images = item.Image,
                Price = price,
                WeightKg = 1.0m,
                IsAuction = false,
                ImmediatePaymentRequired = true,
                IsListed = true,
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            db.Inventories.Add(new Inventory { ProductId = product.Id, Quantity = stock });
            await db.SaveChangesAsync();
            Case(
                stock == 1 ? "BUY-NOW-LAST-ONE" : "BUY-NOW",
                "Product",
                product.Id,
                seller,
                stock == 1
                    ? "Hai buyer cùng mở /Buyer/Products và bấm Pay Now cho món cuối cùng"
                    : "Buyer mở /Buyer/Products, chọn sản phẩm, xem báo giá rồi Pay Now bằng thẻ 4111…",
                stock == 1
                    ? "Chỉ một người được thu tiền và có đơn Paid; người còn lại được hủy giữ tiền"
                    : "Authorize rồi Capture; đơn được tạo thẳng ở trạng thái Paid",
                "buyer"
            );
        }
        db.ChangeTracker.Clear();
    }

    private async Task DebtCasesAsync()
    {
        foreach (var code in new[] { "debt", "overdue", "writeoff", "recovered" })
        {
            var seller = Sellers[code];
            // Debt is due 6h after it starts and becomes write-off eligible after 12h:
            // debt stays "not due" for ~5h, overdue stays unwritten for ~5h after import.
            Clock.Value = Anchor.AddHours(
                code is "writeoff" or "recovered" ? -13
                : code == "overdue" ? -7
                : -1
            );
            var debtAt = Clock.Value;
            // Level 1 holds funds for 4h after delivery; release them before the payout.
            Clock.Value = debtAt.AddHours(-6);
            var order = await OrderAsync(seller, "DEBT-SOURCE-" + code, goods: 50);
            await HistoricalDeliveredAsync(order, debtAt.AddHours(-5));
            Clock.Value = debtAt;
            await Finance.ReleaseDueFundsAsync();
            var account = await db.SellerAccounts.SingleAsync(x => x.SellerId == seller.Id);
            var payout = await Finance.RequestPayoutAsync(
                seller.Id,
                account.AvailableBalance,
                "demo-debt-payout-" + code,
                false
            );
            for (var i = 0; i < 3; i++)
                await Finance.AdvancePayoutsAsync();
            // Refund after funds left the wallet naturally creates a debt cycle.
            await Finance.PlaceHoldAsync(order.Id, "Dữ liệu demo: refund sau khi đã payout");
            await Finance.ResolveHoldAsync(order.Id, false);
            await Returns.RefundAsync(order.Id, "dispute", null);
            Clock.Value = Anchor.AddMinutes(-5);
            await Finance.MaintainDebtsAsync();
            if (code is "writeoff" or "recovered")
                await Finance.MaintainWriteOffsAsync();
            if (code == "recovered")
            {
                account = await db.SellerAccounts.SingleAsync(x => x.SellerId == seller.Id);
                await Finance.RepayDebtAsync(
                    seller.Id,
                    new(
                        account.NegativeBalance,
                        account.NegativeBalance,
                        account.DebtCycleNumber,
                        "demo-debt-recovered",
                        false
                    )
                );
            }
            Case(
                "DEBT-" + code.ToUpperInvariant(),
                "Seller",
                seller.Id,
                seller,
                code == "recovered" ? "Admin xem write-off/thu hồi, Seller vẫn không thể bán/rút"
                    : code == "writeoff"
                        ? "Admin xem quyết định, blacklist và thử đăng ký bằng định danh của seller này"
                    : "Seller xem và trả một phần/toàn bộ nợ; Admin xem hạn chế",
                code switch
                {
                    "debt" => "Nợ chưa đến hạn",
                    "overdue" => "Nợ quá hạn, hạn chế bán/payout",
                    "writeoff" => "Đã write-off, còn nợ và khóa bán vĩnh viễn",
                    _ => "Nợ đã về 0, vẫn khóa bán vĩnh viễn",
                },
                code is "writeoff" or "recovered" ? "admin" : "seller",
                true
            );
            Clock.Value = Anchor;
        }
    }

    private async Task PayoutCasesAsync()
    {
        Clock.Value = Anchor.AddMinutes(-10);
        var seller = Sellers["l3"];
        var completed = await Finance.RequestPayoutAsync(
            seller.Id,
            20,
            "demo-payout-completed",
            false
        );
        for (var i = 0; i < 3; i++)
            await Finance.AdvancePayoutsAsync();
        var returned = await Finance.RequestPayoutAsync(
            seller.Id,
            15,
            "demo-payout-returned",
            true
        );
        for (var i = 0; i < 3; i++)
            await Finance.AdvancePayoutsAsync();
        Clock.Value = Anchor;
        Case(
            "PAYOUT-COMPLETED",
            "Payout",
            completed.Id,
            seller,
            "Seller xem lịch sử từng bước và số đã chi",
            "Completed, trừ ví một lần"
        );
        Case(
            "PAYOUT-RETURNED",
            "Payout",
            returned.Id,
            seller,
            "Seller xem payout ngân hàng trả về",
            "Returned, trả tiền vào ví đúng một lần"
        );
        Case(
            "PAYOUT-READY",
            "Seller",
            seller.Id,
            seller,
            "Seller nhập số tiền, tạo payout thành công hoặc bật mô phỏng lỗi",
            "Available còn đủ; worker chuyển Created/InProgress/FundsSent/Completed hoặc Returned"
        );
    }

    public string Guide()
    {
        var text = new StringBuilder("# Dữ liệu trình diễn G4\n\n");
        text.AppendLine(
            "Chạy G4_Demo.sql trong SSMS để tạo database G4_Demo có đủ schema (baseline + mọi migration hiện có) và dữ liệu demo. "
                + "Không cần chạy baseline, seed-data hay `dotnet ef database update`."
        );
        text.AppendLine(
            "\nChạy lại file bất cứ lúc nào để có bộ dữ liệu sạch: với `@ReplaceExisting = 1` (mặc định) script **xóa G4_Demo cũ rồi tạo lại**. "
                + "Script chỉ chấp nhận tên bắt đầu bằng G4_Demo nên không đụng tới CloneEbayDB. Đặt `@ReplaceExisting = 0` nếu muốn script dừng khi database đã tồn tại. "
                + "Tắt API demo trước khi chạy lại SQL, chạy xong thì bật lại."
        );
        text.AppendLine(
            "\nMật khẩu chung mọi tài khoản: **G4@123456**. Toàn bộ tài khoản, giao dịch và định danh đều giả lập."
        );
        text.AppendLine("\n## Chạy ứng dụng với dữ liệu demo\n");
        text.AppendLine(
            "Mở hai cửa sổ PowerShell tại repo, chạy helper kèm theo (không sửa cấu hình gốc):\n"
        );
        text.AppendLine(
            "```powershell\npowershell -ExecutionPolicy Bypass -File .\\database\\demo\\Run-Demo.ps1 -App Api -SqlServer localhost\n"
                + "powershell -ExecutionPolicy Bypass -File .\\database\\demo\\Run-Demo.ps1 -App Frontend\n```\n"
        );
        text.AppendLine(
            "Mở http://localhost:5137. API demo ở http://localhost:5257. Helper trỏ đúng G4_Demo, dùng HMAC demo và Mail Capture; không gửi thư/call cổng tiền thật. "
                + "Muốn xem thư trong MailHog: bật start-mailhog.cmd rồi thêm -UseMailHog khi chạy -App Api."
        );
        text.AppendLine(
            "Nếu giải nén ở ngoài repo, thêm `-ProjectRoot 'D:\\...\\G4_Project'`. Nếu SQL Server dùng instance khác, thay -SqlServer. Kết nối mặc định Windows Authentication."
        );
        text.AppendLine(
            "\n## Tài khoản\n\n| Email | Role | Dùng để demo | Định danh giả lập |\n| --- | --- | --- | --- |"
        );
        foreach (var login in Logins)
            text.AppendLine(
                $"| {login.Email} | {login.Role} | {login.Description} | {login.Identity ?? "—"} |"
            );
        text.AppendLine(
            "\n## Kịch bản có sẵn\n\n| Mã | Loại/ID | Seller liên quan | Role thao tác đầu | Thao tác | Kết quả |\n| --- | --- | --- | --- | --- | --- |"
        );
        foreach (var c in Cases)
            text.AppendLine(
                $"| {c.Code} | {c.Type} #{c.Id} | {c.SellerEmail} | {c.Role} | {c.Steps} | {c.Expected} |"
            );
        text.AppendLine("\n## Lưu ý lúc trình diễn\n");
        text.AppendLine(
            "- SQL dùng giờ UTC lúc chạy để đặt lại mọi mốc thời gian. Các trạng thái đã chuẩn bị giữ đúng khoảng **4 giờ** sau khi chạy SQL "
                + "(nợ chưa đến hạn còn ~5 giờ, cửa sổ trả hàng/tranh chấp/đổi địa chỉ 6 giờ, đơn chờ thanh toán 6 giờ). Nên chạy SQL trong vòng 1–2 giờ trước buổi demo."
        );
        text.AppendLine(
            "- Profile demo (Run-Demo.ps1): return/receipt/dispute/đổi địa chỉ 6 giờ, giữ tiền cấp 1/2 là 4/2 giờ, nợ đến hạn 6 giờ, write-off 12 giờ, "
                + "giữ hàng chờ thanh toán 6 giờ, 2 điểm phạt Unpaid Item trong 30 ngày thì khóa. Đây là cấu hình demo riêng, không thay mặc định nghiệp vụ của repo."
        );
        text.AppendLine(
            "- Đơn chưa thanh toán không tự hết hạn (Unpaid Item): quá hạn thì seller bấm Hủy đơn quá hạn thanh toán và buyer bị cộng điểm phạt. "
                + "UNPAID-BAN làm buyer.other@demo.test bị khóa; không dùng tài khoản này cho phần khác sau khi đã chạy ca đó."
        );
        text.AppendLine(
            "- Best Offer: sản phẩm 1/2/3 của seller.l1 nhận trả giá; sản phẩm 1 có ngưỡng tự từ chối dưới 50 USD và tự chấp nhận từ 95 USD. "
                + "Mỗi buyer tối đa 5 lượt/sản phẩm. Buy It Now: hai sản phẩm Immediate Payment của seller.l1 ở /Buyer/Products. Klarna/PayPal cần sandbox riêng nên không có dữ liệu sẵn."
        );
        text.AppendLine(
            "- Tự xét level đặt 24 giờ để không nâng các seller up2/up3 trước khi bấm. Bấm Xét lại vẫn chạy service thật. Seller l2/l3 có 20/100 đơn và feedback/tracking hỗ trợ, không chỉ gán số level."
        );
        text.AppendLine(
            "- Write-off có sẵn được tạo bằng service sau refund khi tiền đã payout, giữ cả ledger/nợ/history/blacklist. Có seller đã trả hết sau write-off nhưng vẫn bị khóa."
        );
        text.AppendLine(
            "- Để thử blacklist dùng định danh seller.writeoff hoặc nút mẫu blacklist 999000000002. Phải dùng khóa HMAC demo do helper cấp; chỉ đổi DB mà dùng khóa local khác sẽ bị từ chối an toàn."
        );
        text.AppendLine(
            "- Không tạo PayPal Succeeded/Verifying giả. Có thể chọn thẻ mô phỏng 4111111111111111, expiry 12/30; 4000000000000002 để bị từ chối. PayPal thật cần credential Sandbox riêng."
        );
        text.AppendLine(
            "- Link example.com trong dispute chỉ là link bằng chứng mô phỏng, không phải ảnh đã được xác minh. Email Capture hiển thị trong app, không gửi SMTP. Hộp chuông sinh từ outbox thực."
        );
        text.AppendLine(
            "- INR1, FR1, LIM1, A2 và RT2 đầy đủ chưa có source: SQL không tạo thêm chức năng hoặc trạng thái giả cho các nhánh này. Dữ liệu bao phủ các nhánh hiện có, không mô phỏng lỗi mạng/đồng thời."
        );
        text.AppendLine(
            "- Quota đăng nhập là 10 lần/phút/IP. Khi đổi nhiều tài khoản liên tiếp có thể phải đợi 429; các tài khoản đã đăng nhập có quota riêng theo UserId."
        );
        text.AppendLine(
            "- Dữ liệu sẽ thay đổi khi worker chạy/thao tác. Muốn quay lại trạng thái ban đầu chỉ cần chạy lại G4_Demo.sql (mặc định xóa và tạo lại G4_Demo). "
                + "Muốn giữ bản cũ thì đổi @DatabaseName sang tên khác bắt đầu bằng G4_Demo và truyền -DatabaseName tương ứng cho helper."
        );
        text.AppendLine(
            "\n## Tra cứu từ SQL\n\n```sql\nUSE G4_Demo;\nSELECT * FROM demo.Scenario ORDER BY Code;\nSELECT * FROM demo.LoginAccount ORDER BY Email;\n```\n"
        );
        return text.ToString();
    }
}

internal sealed record DemoCase(
    string Code,
    string Type,
    long Id,
    string SellerEmail,
    string Role,
    string Steps,
    string Expected,
    bool Timed
);

internal sealed record DemoLogin(string Email, string Role, string Description, string? Identity);

internal sealed class DemoClock : TimeProvider
{
    public DateTime Value;

    public override DateTimeOffset GetUtcNow() => new(Value);
}

internal sealed class DemoCarrier : ICarrierGateway
{
    public Task<string> CreateLabelAsync(
        int orderId,
        string direction,
        string key,
        CancellationToken ct
    ) => Task.FromResult($"G4-{orderId}-{(direction == "Outbound" ? "O" : "R")}");
}

internal sealed class DemoRefund : IRefundGateway
{
    public Task<string> RefundAsync(
        Payment payment,
        decimal amount,
        string key,
        CancellationToken ct
    ) =>
        payment.Method == "Card"
            ? Task.FromResult("CARD-REFUND-DEMO-" + key)
            : throw new InvalidOperationException(
                "Demo generator never calls external payment providers."
            );
}

internal sealed class DemoEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "G4.DemoData";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed class DemoDbFactory(string connection) : IDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
}

internal sealed class NeverEmail : IEmailSender
{
    public Task SendAsync(EmailEnvelope message, CancellationToken ct) =>
        throw new InvalidOperationException("SMTP is not allowed in the demo generator.");
}
