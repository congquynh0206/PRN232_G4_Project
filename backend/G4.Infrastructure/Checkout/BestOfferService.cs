using System.Data;
using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Checkout;

public sealed class BestOfferService(
    ApplicationDbContext db,
    ISellerFinanceService finance,
    TimeProvider clock
) : IBestOfferService
{
    private const int BuyerOfferLimit = 5;
    private const int OfferLifetimeHours = 24;
    private const string PendingSeller = "PendingSeller";
    private const string PendingBuyer = "PendingBuyer";
    private static readonly string[] ActiveStatuses = [PendingSeller, PendingBuyer];
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<OfferableProductView>> GetOfferableProductsAsync(
        CancellationToken ct
    ) =>
        await (
            from product in db.Products.AsNoTracking()
            join stock in db.Inventories.AsNoTracking()
                on product.Id equals stock.ProductId
            where
                product.AllowOffers
                && product.IsAuction != true
                && product.Price != null
                && stock.Quantity > 0
            orderby product.Id
            select new OfferableProductView(
                product.Id,
                product.Title ?? "Sản phẩm",
                product.Images,
                product.Price!.Value,
                product.WeightKg ?? 0,
                stock.Quantity ?? 0,
                product.SellerId ?? 0
            )
        ).ToListAsync(ct);

    public Task<IReadOnlyList<BestOfferView>> GetBuyerOffersAsync(
        int buyerId,
        CancellationToken ct
    ) => GetOffersAsync(x => x.BuyerId == buyerId, "buyer", ct);

    public Task<IReadOnlyList<BestOfferView>> GetSellerOffersAsync(
        int sellerId,
        CancellationToken ct
    ) => GetOffersAsync(x => x.SellerId == sellerId, "seller", ct);

    public async Task<BestOfferView> CreateAsync(
        int buyerId,
        BestOfferCreateInput input,
        CancellationToken ct
    )
    {
        ValidateKey(input.Key);
        ValidateAmount(input.Amount);
        ValidateCard(input.CardProfile, input.CardExpiry);
        if (input.PaymentMethod != "Card")
            throw new ArgumentException(
                "Best Offer hiện yêu cầu thẻ giả lập đã lưu để tự thanh toán khi được chấp nhận."
            );

        await using var tx = await BeginTransactionAsync(ct);
        var duplicate = await db.BestOffers.SingleOrDefaultAsync(
            x => x.CreationKey == input.Key,
            ct
        );
        if (duplicate is not null)
        {
            if (duplicate.BuyerId != buyerId || duplicate.ProductId != input.ProductId)
                throw new InvalidOperationException("Khóa gửi giá đã được dùng cho yêu cầu khác.");
            return await ViewAsync(duplicate, "buyer", ct);
        }

        var product = await db.Products.SingleOrDefaultAsync(
            x => x.Id == input.ProductId && x.AllowOffers && x.IsAuction != true,
            ct
        ) ?? throw new KeyNotFoundException("Sản phẩm không nhận trả giá.");
        if (product.SellerId is null || product.Price is null || input.Amount >= product.Price)
            throw new ArgumentException("Giá trả phải thấp hơn giá niêm yết.");
        if (product.WeightKg is null or <= 0)
            throw new InvalidOperationException("Sản phẩm chưa có khối lượng hợp lệ.");
        var available = await db.Inventories.AnyAsync(
            x => x.ProductId == product.Id && x.Quantity > 0,
            ct
        );
        if (!available)
            throw new InvalidOperationException("Sản phẩm hiện đã hết hàng.");
        if (
            !await db.Addresses.AnyAsync(
                x => x.Id == input.AddressId && x.UserId == buyerId,
                ct
            )
        )
            throw new ArgumentException("Chọn địa chỉ giao hàng hợp lệ trước khi trả giá.");
        var active = await db.BestOffers.AnyAsync(
            x =>
                x.ProductId == product.Id
                && x.BuyerId == buyerId
                && ActiveStatuses.Contains(x.Status),
            ct
        );
        if (active)
            throw new InvalidOperationException(
                "Bạn đã có một phiên trả giá đang mở cho sản phẩm này."
            );

        var offer = new BestOffer
        {
            ProductId = product.Id,
            BuyerId = buyerId,
            SellerId = product.SellerId.Value,
            AddressId = input.AddressId,
            CurrentAmount = input.Amount,
            CurrentActorId = buyerId,
            Status = PendingSeller,
            BuyerOfferCount = 1,
            PaymentMethod = "Card",
            CardProfile = input.CardProfile,
            CardExpiry = input.CardExpiry,
            CreationKey = input.Key,
            CreatedAt = Now,
            UpdatedAt = Now,
            ExpiresAt = Now.AddHours(OfferLifetimeHours),
        };
        db.BestOffers.Add(offer);
        await db.SaveChangesAsync(ct);
        var turn = AddTurn(offer, buyerId, input.Amount, "Offer", input.Key);

        if (product.AutoDeclineOfferBelow is decimal floor && input.Amount < floor)
        {
            offer.Status = "Declined";
            turn.Result = "AutoDeclined";
        }
        else if (product.AutoAcceptOfferPrice is decimal acceptAt && input.Amount >= acceptAt)
        {
            await AcceptAndChargeAsync(offer, product, input.AddressId, turn, ct);
        }
        else
        {
            AddChange(
                offer,
                "Đề nghị trả giá mới",
                $"Có đề nghị trả giá cho sản phẩm #{product.Id}.",
                "best-offers"
            );
        }

        offer.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
        return await ViewAsync(offer, "buyer", ct);
    }

    public async Task<BestOfferView> SellerAcceptAsync(
        int sellerId,
        long id,
        CancellationToken ct
    )
    {
        await using var tx = await BeginTransactionAsync(ct);
        var offer = await GetOfferAsync(id, sellerId, "seller", ct);
        RequireStatus(offer, PendingSeller);
        var turn = AddTurn(offer, sellerId, offer.CurrentAmount, "Accept");
        var product = await ProductForOfferAsync(offer, ct);
        await AcceptAndChargeAsync(offer, product, offer.AddressId, turn, ct);
        offer.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
        return await ViewAsync(offer, "seller", ct);
    }

    public Task<BestOfferView> SellerDeclineAsync(int sellerId, long id, CancellationToken ct) =>
        CloseAsync(sellerId, id, "seller", "Declined", "Decline", ct);

    public async Task<BestOfferView> SellerCounterAsync(
        int sellerId,
        long id,
        BestOfferActionInput input,
        CancellationToken ct
    )
    {
        ValidateAmount(input.Amount ?? 0m);
        ValidateKey(input.Key);
        await using var tx = await BeginTransactionAsync(ct);
        var offer = await GetOfferAsync(id, sellerId, "seller", ct);
        RequireStatus(offer, PendingSeller);
        var product = await ProductForOfferAsync(offer, ct);
        if (input.Amount > product.Price)
            throw new ArgumentException("Giá phản hồi không được cao hơn giá niêm yết.");
        offer.CurrentAmount = input.Amount!.Value;
        offer.CurrentActorId = sellerId;
        offer.Status = PendingBuyer;
        RefreshDeadline(offer);
        AddTurn(offer, sellerId, offer.CurrentAmount, "Counter", input.Key);
        AddChange(offer, "Người bán đã phản hồi giá", $"Có phản hồi cho đề nghị #{offer.Id}.", "best-offers");
        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
        return await ViewAsync(offer, "seller", ct);
    }

    public async Task<BestOfferView> BuyerAcceptAsync(int buyerId, long id, CancellationToken ct)
    {
        await using var tx = await BeginTransactionAsync(ct);
        var offer = await GetOfferAsync(id, buyerId, "buyer", ct);
        RequireStatus(offer, PendingBuyer);
        var turn = AddTurn(offer, buyerId, offer.CurrentAmount, "Accept");
        var product = await ProductForOfferAsync(offer, ct);
        await AcceptAndChargeAsync(offer, product, offer.AddressId, turn, ct);
        offer.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
        return await ViewAsync(offer, "buyer", ct);
    }

    public async Task<BestOfferView> UpdatePaymentProfileAsync(
        int buyerId,
        long id,
        BestOfferActionInput input,
        CancellationToken ct
    )
    {
        ValidateCard(input.CardProfile, input.CardExpiry);
        await using var tx = await BeginTransactionAsync(ct);
        var offer = await GetOfferAsync(id, buyerId, "buyer", ct);
        if (!ActiveStatuses.Contains(offer.Status) || offer.ExpiresAt <= Now)
            throw new InvalidOperationException("Chỉ có thể cập nhật thẻ cho phiên đang mở.");
        offer.CardProfile = input.CardProfile;
        offer.CardExpiry = input.CardExpiry;
        offer.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
        return await ViewAsync(offer, "buyer", ct);
    }

    public Task<BestOfferView> BuyerDeclineAsync(int buyerId, long id, CancellationToken ct) =>
        CloseAsync(buyerId, id, "buyer", "Declined", "Decline", ct);

    public async Task<BestOfferView> BuyerCounterAsync(
        int buyerId,
        long id,
        BestOfferActionInput input,
        CancellationToken ct
    )
    {
        ValidateAmount(input.Amount ?? 0m);
        ValidateKey(input.Key);
        await using var tx = await BeginTransactionAsync(ct);
        var offer = await GetOfferAsync(id, buyerId, "buyer", ct);
        RequireStatus(offer, PendingBuyer);
        var product = await ProductForOfferAsync(offer, ct);
        if (input.Amount >= product.Price)
            throw new ArgumentException("Giá trả phải thấp hơn giá niêm yết.");
        if (offer.BuyerOfferCount >= BuyerOfferLimit)
            throw new InvalidOperationException("Bạn đã dùng hết 5 lượt trả giá cho phiên này.");
        offer.BuyerOfferCount++;
        offer.CurrentAmount = input.Amount!.Value;
        offer.CurrentActorId = buyerId;
        offer.Status = PendingSeller;
        RefreshDeadline(offer);
        var turn = AddTurn(offer, buyerId, offer.CurrentAmount, "Counter", input.Key);
        if (product.AutoDeclineOfferBelow is decimal floor && offer.CurrentAmount < floor)
        {
            offer.Status = "Declined";
            turn.Result = "AutoDeclined";
        }
        else if (product.AutoAcceptOfferPrice is decimal acceptAt && offer.CurrentAmount >= acceptAt)
        {
            await AcceptAndChargeAsync(offer, product, offer.AddressId, turn, ct);
        }
        else
        {
            AddChange(
                offer,
                "Người mua đã trả giá lại",
                $"Có giá mới cho đề nghị #{offer.Id}.",
                "best-offers"
            );
        }
        offer.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
        return await ViewAsync(offer, "buyer", ct);
    }

    public async Task<int> ExpireAsync(CancellationToken ct)
    {
        var due = await db.BestOffers
            .Where(x => ActiveStatuses.Contains(x.Status) && x.ExpiresAt <= Now)
            .OrderBy(x => x.ExpiresAt)
            .Take(100)
            .ToListAsync(ct);
        foreach (var offer in due)
        {
            offer.Status = "Expired";
            offer.UpdatedAt = Now;
            AddChange(offer, "Đề nghị trả giá hết hạn", $"Đề nghị #{offer.Id} đã hết hạn.", "best-offers");
        }
        if (due.Count > 0)
            await db.SaveChangesAsync(ct);
        return due.Count;
    }

    public async Task UpdateProductSettingsAsync(
        int sellerId,
        BestOfferSettingsInput input,
        CancellationToken ct
    )
    {
        var product = await db.Products.SingleOrDefaultAsync(
            x => x.Id == input.ProductId && x.SellerId == sellerId,
            ct
        ) ?? throw new KeyNotFoundException("Không tìm thấy sản phẩm của cửa hàng.");
        if (input.AllowOffers)
        {
            if (product.IsAuction == true || product.Price is null or <= 0)
                throw new InvalidOperationException("Chỉ mặt hàng bán giá cố định nhận Best Offer.");
            if (
                input.AutoAcceptPrice is decimal accept
                && (accept <= 0 || accept > product.Price)
            )
                throw new ArgumentException(
                    "Ngưỡng tự động chấp nhận phải nằm trên 0 và không vượt giá niêm yết."
                );
            if (
                input.AutoDeclineBelow is decimal decline
                && (decline < 0 || decline >= product.Price)
            )
                throw new ArgumentException("Ngưỡng tự động từ chối phải thấp hơn giá niêm yết.");
            if (
                input.AutoAcceptPrice is decimal acceptPrice
                && input.AutoDeclineBelow is decimal declinePrice
                && declinePrice >= acceptPrice
            )
                throw new ArgumentException("Ngưỡng từ chối phải thấp hơn ngưỡng chấp nhận.");
        }
        product.AllowOffers = input.AllowOffers;
        product.AutoAcceptOfferPrice = input.AllowOffers ? input.AutoAcceptPrice : null;
        product.AutoDeclineOfferBelow = input.AllowOffers ? input.AutoDeclineBelow : null;
        await db.SaveChangesAsync(ct);
    }

    private async Task AcceptAndChargeAsync(
        BestOffer offer,
        Product product,
        int addressId,
        BestOfferTurn turn,
        CancellationToken ct
    )
    {
        ValidateCard(offer.CardProfile, offer.CardExpiry);
        var cardNumber = offer.CardProfile == "Declined"
            ? "4000000000000002"
            : "4111111111111111";
        if (CardPaymentSimulator.Outcome(cardNumber, offer.CardExpiry!) != "Succeeded")
        {
            turn.Result = "PaymentFailed";
            throw new InvalidOperationException("Thẻ giả lập không được chấp nhận; chưa chốt giao dịch.");
        }

        await finance.ValidateNewSaleAsync(offer.SellerId, ct);
        var buyerAddress = await db.Addresses.SingleOrDefaultAsync(
            x => x.Id == addressId && x.UserId == offer.BuyerId,
            ct
        ) ?? throw new InvalidOperationException("Địa chỉ giao hàng không còn hợp lệ.");
        var pickup = await db.Addresses.Where(x => x.UserId == offer.SellerId && x.IsDefault == true)
            .OrderBy(x => x.Id).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Người bán cần có địa chỉ lấy hàng mặc định.");
        var stock = await db.Inventories.SingleOrDefaultAsync(x => x.ProductId == product.Id, ct)
            ?? throw new InvalidOperationException("Không tìm thấy tồn kho của sản phẩm.");
        if (stock.Quantity is null or < 1)
            throw new InvalidOperationException("Sản phẩm vừa hết hàng; chưa chốt đề nghị.");

        var quote = OrderPricing.Calculate(
            [new PriceLine(offer.CurrentAmount, 1, product.WeightKg!.Value)],
            0,
            ShippingPricing.IsHanoiLocal(
                pickup.State,
                pickup.Country,
                buyerAddress.State,
                buyerAddress.Country
            )
        );
        stock.Quantity--;
        stock.LastUpdated = Now;
        var order = new OrderTable
        {
            BuyerId = offer.BuyerId,
            SellerId = offer.SellerId,
            AddressId = buyerAddress.Id,
            AddressSnapshot = AddressText(buyerAddress),
            PickupAddressSnapshot = AddressText(pickup),
            DeliveryStateSnapshot = buyerAddress.State,
            DeliveryCountrySnapshot = buyerAddress.Country,
            PickupStateSnapshot = pickup.State,
            PickupCountrySnapshot = pickup.Country,
            OrderDate = Now,
            UpdatedAt = Now,
            Subtotal = quote.Subtotal,
            ShippingFee = quote.Shipping,
            ShippingBase = quote.Shipping,
            TotalWeightKg = quote.TotalWeightKg,
            TotalPrice = quote.Total,
            SellerGrossSnapshot = quote.Total,
            PricingSchemaVersion = 1,
            Currency = "USD",
            CheckoutKey = $"best-offer-{offer.Id}",
            Status = "AwaitingPayment",
        };
        order.OrderItems.Add(new OrderItem
        {
            ProductId = product.Id,
            Quantity = 1,
            UnitPrice = offer.CurrentAmount,
            ProductTitleSnapshot = product.Title,
            UnitWeightKgSnapshot = product.WeightKg,
            SellerIdSnapshot = offer.SellerId,
        });
        db.OrderTables.Add(order);
        await db.SaveChangesAsync(ct);

        var payment = new Payment
        {
            OrderId = order.Id,
            UserId = offer.BuyerId,
            Amount = quote.Total,
            Method = "Card",
            Status = "Succeeded",
            ProviderTransactionId = $"OFFER-CARD-{Guid.NewGuid():N}",
            IdempotencyKey = $"best-offer-payment-{offer.Id}",
            CreatedAt = Now,
            UpdatedAt = Now,
            PaidAt = Now,
        };
        db.Payments.Add(payment);
        order.Status = "Paid";
        offer.Status = "Accepted";
        offer.OrderId = order.Id;
        turn.Result = "AcceptedAndPaid";
        AddChange(
            offer,
            "Best Offer đã chốt và thanh toán",
            $"Đơn hàng #{order.Id} đã được tạo và thanh toán.",
            "orders"
        );
        AddChange(
            offer,
            "Đề nghị đã được chấp nhận",
            $"Đề nghị #{offer.Id} đã được chấp nhận.",
            "best-offers"
        );
        await db.SaveChangesAsync(ct);
        await finance.ValidateMonthlyLimitAsync(order.Id, ct);
        await finance.RecordSuccessfulPaymentAsync(order.Id, payment.Id, ct);
        offer.UpdatedAt = Now;
    }

    private async Task<BestOfferView> CloseAsync(
        int actorId,
        long id,
        string role,
        string status,
        string action,
        CancellationToken ct
    )
    {
        await using var tx = await BeginTransactionAsync(ct);
        var offer = await GetOfferAsync(id, actorId, role, ct);
        RequireStatus(offer, role == "seller" ? PendingSeller : PendingBuyer);
        offer.Status = status;
        offer.UpdatedAt = Now;
        AddTurn(offer, actorId, offer.CurrentAmount, action);
        AddChange(offer, "Đề nghị trả giá đã đóng", $"Đề nghị #{offer.Id} đã được {status}.", "best-offers");
        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
        return await ViewAsync(offer, role, ct);
    }

    private async Task<IReadOnlyList<BestOfferView>> GetOffersAsync(
        System.Linq.Expressions.Expression<Func<BestOffer, bool>> predicate,
        string role,
        CancellationToken ct
    )
    {
        var offers = await db.BestOffers.AsNoTracking().Where(predicate)
            .OrderByDescending(x => x.UpdatedAt).Take(100).ToListAsync(ct);
        var result = new List<BestOfferView>(offers.Count);
        foreach (var offer in offers)
            result.Add(await ViewAsync(offer, role, ct));
        return result;
    }

    private async Task<BestOfferView> ViewAsync(BestOffer offer, string role, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().SingleAsync(x => x.Id == offer.ProductId, ct);
        var turns = await db.BestOfferTurns.AsNoTracking()
            .Where(x => x.BestOfferId == offer.Id).OrderBy(x => x.Id)
            .Select(x => new BestOfferTurnView(
                x.Id,
                x.ActorId,
                x.ActorId == offer.SellerId ? "seller" : "buyer",
                x.Amount,
                x.Action,
                x.Result,
                x.CreatedAt
            )).ToListAsync(ct);
        return new BestOfferView(
            offer.Id,
            offer.ProductId,
            product.Title ?? "Sản phẩm",
            offer.BuyerId,
            offer.SellerId,
            product.Price ?? 0,
            offer.CurrentAmount,
            offer.Status,
            offer.BuyerOfferCount,
            BuyerOfferLimit,
            offer.ExpiresAt,
            offer.OrderId,
            role,
            turns
        );
    }

    private async Task<BestOffer> GetOfferAsync(
        long id,
        int actorId,
        string role,
        CancellationToken ct
    ) => await db.BestOffers.SingleOrDefaultAsync(
        x => x.Id == id && (role == "buyer" ? x.BuyerId == actorId : x.SellerId == actorId),
        ct
    ) ?? throw new KeyNotFoundException("Không tìm thấy đề nghị trả giá.");

    private Task<Product> ProductForOfferAsync(BestOffer offer, CancellationToken ct) =>
        db.Products.SingleAsync(x => x.Id == offer.ProductId, ct);

    private async Task<int> DefaultAddressIdAsync(int buyerId, CancellationToken ct)
    {
        var id = await db.Addresses.Where(x => x.UserId == buyerId && x.IsDefault == true)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (id == 0)
            throw new InvalidOperationException("Buyer cần có địa chỉ giao hàng mặc định.");
        return id;
    }

    private BestOfferTurn AddTurn(
        BestOffer offer,
        int actorId,
        decimal amount,
        string action,
        string? key = null
    )
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            var existing = db.BestOfferTurns.Local.FirstOrDefault(x => x.RequestKey == key);
            if (existing is not null)
                return existing;
        }
        var turn = new BestOfferTurn
        {
            BestOfferId = offer.Id,
            ActorId = actorId,
            Amount = amount,
            Action = action,
            RequestKey = key,
            CreatedAt = Now,
        };
        db.BestOfferTurns.Add(turn);
        return turn;
    }

    private void AddChange(BestOffer offer, string title, string message, string topic) =>
        db.CommerceChanges.Add(new CommerceChange
        {
            Topic = topic,
            BuyerId = offer.BuyerId,
            SellerId = offer.SellerId,
            OrderId = offer.OrderId,
            CreatedAt = Now,
            NotificationTitle = title,
            NotificationMessage = message,
        });

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken ct
    ) => db.Database.IsRelational() && db.Database.CurrentTransaction is null
        ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
        : null;

    private void RefreshDeadline(BestOffer offer)
    {
        offer.ExpiresAt = Now.AddHours(OfferLifetimeHours);
        offer.UpdatedAt = Now;
    }

    private void RequireStatus(BestOffer offer, string expected)
    {
        if (offer.Status != expected)
            throw new InvalidOperationException("Lượt trả giá hiện không chờ vai trò của bạn.");
        if (offer.ExpiresAt <= Now)
            throw new InvalidOperationException("Lượt trả giá đã hết hạn.");
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0 || amount > 99_999_999.99m || decimal.Round(amount, 2) != amount)
            throw new ArgumentException("Giá trả phải lớn hơn 0 và có tối đa 2 chữ số thập phân.");
    }

    private static void ValidateKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            throw new ArgumentException("Cần khóa thao tác hợp lệ để chống gửi trùng.");
    }

    private static void ValidateCard(string? profile, string? expiry)
    {
        if (profile is not ("Success" or "Declined"))
            throw new ArgumentException("Chọn một thẻ thử hợp lệ.");
        if (string.IsNullOrWhiteSpace(expiry) || expiry.Length > 5)
            throw new ArgumentException("Hạn thẻ thử không hợp lệ.");
    }

    private static string AddressText(Address address) => string.Join(
        ", ",
        new[] { address.FullName, address.Street, address.City, address.State, address.Country }
            .Where(x => !string.IsNullOrWhiteSpace(x))
    );
}
