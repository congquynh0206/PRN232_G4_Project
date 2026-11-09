using System.Data;
using System.Globalization;
using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Checkout;

public sealed class BestOfferService(
    ApplicationDbContext db,
    ISellerFinanceService finance,
    TimeProvider clock,
    IConfiguration? config = null
) : IBestOfferService
{
    // eBay-style limit: one buyer may send at most 5 offers for one product, across sessions.
    public const int BuyerOfferLimit = 5;

    // Auto-Pay charges the saved test card on accept; PayLater creates an unpaid order.
    public const string AutoPayMethod = "Card";
    public const string PayLaterMethod = "PayLater";

    private const int OfferLifetimeHours = 24;
    private const string PendingSeller = "PendingSeller";
    private const string PendingBuyer = "PendingBuyer";
    private static readonly string[] ActiveStatuses = [PendingSeller, PendingBuyer];
    private static readonly string[] BuyerOfferActions = ["Offer", "Counter"];
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private int PaymentHoldMinutes =>
        UnpaidItemRules.NormalizeHoldMinutes(
            config?.GetValue<int?>("Checkout:PaymentHoldMinutes")
        );

    public async Task<IReadOnlyList<OfferableProductView>> GetOfferableProductsAsync(
        int buyerId,
        CancellationToken ct
    )
    {
        var products = await (
            from product in db.Products.AsNoTracking()
            join stock in db.Inventories.AsNoTracking()
                on product.Id equals stock.ProductId
            where
                product.AllowOffers
                && product.IsAuction != true
                && product.Price != null
                && stock.Quantity > 0
            orderby product.Id
            select new
            {
                product.Id,
                Title = product.Title ?? "Sản phẩm",
                product.Images,
                Price = product.Price!.Value,
                WeightKg = product.WeightKg ?? 0,
                Available = stock.Quantity ?? 0,
                SellerId = product.SellerId ?? 0,
            }
        ).ToListAsync(ct);
        var used = await (
            from turn in db.BestOfferTurns.AsNoTracking()
            join offer in db.BestOffers.AsNoTracking() on turn.BestOfferId equals offer.Id
            where
                offer.BuyerId == buyerId
                && turn.ActorId == buyerId
                && BuyerOfferActions.Contains(turn.Action)
            group turn by offer.ProductId into productTurns
            select new { ProductId = productTurns.Key, Count = productTurns.Count() }
        ).ToDictionaryAsync(x => x.ProductId, x => x.Count, ct);
        return products
            .Select(x => new OfferableProductView(
                x.Id,
                x.Title,
                x.Images,
                x.Price,
                x.WeightKg,
                x.Available,
                x.SellerId,
                used.GetValueOrDefault(x.Id),
                BuyerOfferLimit
            ))
            .ToList();
    }

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
        var payLater = IsPayLater(input.PaymentMethod);
        if (!payLater)
            ValidateCard(input.CardProfile, input.CardExpiry);

        await using var tx = await BeginTransactionAsync(ct);
        var duplicate = await db.BestOffers.SingleOrDefaultAsync(
            x => x.CreationKey == input.Key,
            ct
        );
        if (duplicate is not null)
        {
            if (duplicate.BuyerId != buyerId || duplicate.ProductId != input.ProductId)
                throw new InvalidOperationException("Khóa gửi giá đã được dùng cho yêu cầu khác.");
            return await CommitViewAsync(tx, duplicate, "buyer", ct);
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
        // Read inside the serializable transaction, so parallel offers cannot pass the limit.
        await EnsureOfferQuotaAsync(buyerId, product.Id, ct);

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
            PaymentMethod = payLater ? PayLaterMethod : AutoPayMethod,
            CardProfile = payLater ? null : input.CardProfile,
            CardExpiry = payLater ? null : input.CardExpiry,
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
            await AcceptAsync(offer, product, turn, ct);
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
        return await CommitViewAsync(tx, offer, "buyer", ct);
    }

    public async Task<BestOfferView> SellerAcceptAsync(
        int sellerId,
        long id,
        BestOfferActionInput input,
        CancellationToken ct
    )
    {
        ValidateKey(input.Key);
        await using var tx = await BeginTransactionAsync(ct);
        var offer = await GetOfferAsync(id, sellerId, "seller", ct);
        if (await IsReplayAsync(offer, sellerId, input.Key!, ct))
            return await CommitViewAsync(tx, offer, "seller", ct);
        RequireStatus(offer, PendingSeller);
        var turn = AddTurn(offer, sellerId, offer.CurrentAmount, "Accept", input.Key);
        var product = await ProductForOfferAsync(offer, ct);
        await AcceptAsync(offer, product, turn, ct);
        offer.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return await CommitViewAsync(tx, offer, "seller", ct);
    }

    public Task<BestOfferView> SellerDeclineAsync(
        int sellerId,
        long id,
        BestOfferActionInput input,
        CancellationToken ct
    ) => CloseAsync(sellerId, id, "seller", input.Key, ct);

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
        if (await IsReplayAsync(offer, sellerId, input.Key!, ct))
            return await CommitViewAsync(tx, offer, "seller", ct);
        RequireStatus(offer, PendingSeller);
        var product = await ProductForOfferAsync(offer, ct);
        var amount = input.Amount!.Value;
        // A counteroffer sits between the buyer's offer and the list price.
        if (amount <= offer.CurrentAmount || amount >= product.Price)
            throw new ArgumentException(
                $"Giá phản hồi phải lớn hơn {Money(offer.CurrentAmount)} (giá người mua đưa) "
                    + $"và nhỏ hơn {Money(product.Price ?? 0)} (giá niêm yết)."
            );
        offer.CurrentAmount = amount;
        offer.CurrentActorId = sellerId;
        offer.Status = PendingBuyer;
        RefreshDeadline(offer);
        AddTurn(offer, sellerId, amount, "Counter", input.Key);
        AddChange(
            offer,
            "Người bán đã phản hồi giá",
            $"Có phản hồi cho đề nghị #{offer.Id}.",
            "best-offers"
        );
        await db.SaveChangesAsync(ct);
        return await CommitViewAsync(tx, offer, "seller", ct);
    }

    public async Task<BestOfferView> BuyerAcceptAsync(
        int buyerId,
        long id,
        BestOfferActionInput input,
        CancellationToken ct
    )
    {
        ValidateKey(input.Key);
        await using var tx = await BeginTransactionAsync(ct);
        var offer = await GetOfferAsync(id, buyerId, "buyer", ct);
        if (await IsReplayAsync(offer, buyerId, input.Key!, ct))
            return await CommitViewAsync(tx, offer, "buyer", ct);
        RequireStatus(offer, PendingBuyer);
        var turn = AddTurn(offer, buyerId, offer.CurrentAmount, "Accept", input.Key);
        var product = await ProductForOfferAsync(offer, ct);
        await AcceptAsync(offer, product, turn, ct);
        offer.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return await CommitViewAsync(tx, offer, "buyer", ct);
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
        if (offer.PaymentMethod != AutoPayMethod)
            throw new InvalidOperationException(
                "Phiên thanh toán sau không dùng thẻ Auto-Pay; trả tiền trong Đơn hàng."
            );
        offer.CardProfile = input.CardProfile;
        offer.CardExpiry = input.CardExpiry;
        offer.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return await CommitViewAsync(tx, offer, "buyer", ct);
    }

    public Task<BestOfferView> BuyerDeclineAsync(
        int buyerId,
        long id,
        BestOfferActionInput input,
        CancellationToken ct
    ) => CloseAsync(buyerId, id, "buyer", input.Key, ct);

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
        if (await IsReplayAsync(offer, buyerId, input.Key!, ct))
            return await CommitViewAsync(tx, offer, "buyer", ct);
        RequireStatus(offer, PendingBuyer);
        await EnsureOfferQuotaAsync(buyerId, offer.ProductId, ct);
        var product = await ProductForOfferAsync(offer, ct);
        var amount = input.Amount!.Value;
        var previous = await LastBuyerAmountAsync(offer, ct);
        // The buyer moves up from their last offer but stays below the seller's counter.
        if (amount <= previous || amount >= offer.CurrentAmount)
            throw new ArgumentException(
                $"Giá trả lại phải lớn hơn {Money(previous)} (giá bạn đưa lần trước) "
                    + $"và nhỏ hơn {Money(offer.CurrentAmount)} (giá người bán phản hồi)."
            );
        offer.BuyerOfferCount++;
        offer.CurrentAmount = amount;
        offer.CurrentActorId = buyerId;
        offer.Status = PendingSeller;
        RefreshDeadline(offer);
        var turn = AddTurn(offer, buyerId, amount, "Counter", input.Key);
        if (product.AutoDeclineOfferBelow is decimal floor && amount < floor)
        {
            offer.Status = "Declined";
            turn.Result = "AutoDeclined";
        }
        else if (product.AutoAcceptOfferPrice is decimal acceptAt && amount >= acceptAt)
        {
            await AcceptAsync(offer, product, turn, ct);
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
        return await CommitViewAsync(tx, offer, "buyer", ct);
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
            AddChange(
                offer,
                "Đề nghị trả giá hết hạn",
                $"Đề nghị #{offer.Id} đã hết hạn.",
                "best-offers"
            );
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
                throw new InvalidOperationException(
                    "Chỉ mặt hàng bán giá cố định nhận Best Offer."
                );
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

    private Task AcceptAsync(
        BestOffer offer,
        Product product,
        BestOfferTurn turn,
        CancellationToken ct
    ) =>
        offer.PaymentMethod == PayLaterMethod
            ? AcceptUnpaidAsync(offer, product, turn, ct)
            : AcceptAndChargeAsync(offer, product, turn, ct);

    // Auto-Pay: the buyer authorized the saved test card when making the offer.
    private async Task AcceptAndChargeAsync(
        BestOffer offer,
        Product product,
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
            throw new InvalidOperationException(
                "Thẻ giả lập không được chấp nhận; chưa chốt giao dịch."
            );
        }

        var order = await CreateOrderAsync(offer, product, null, ct);
        var payment = new Payment
        {
            OrderId = order.Id,
            UserId = offer.BuyerId,
            Amount = order.TotalPrice,
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

    // Pay later: the accepted offer becomes an unpaid order that holds the stock until the
    // normal payment deadline. Payment, expiry, seller cancel and strikes reuse that flow.
    private async Task AcceptUnpaidAsync(
        BestOffer offer,
        Product product,
        BestOfferTurn turn,
        CancellationToken ct
    )
    {
        var banned = await db
            .Users.Where(x => x.Id == offer.BuyerId)
            .Select(x => x.BannedAt)
            .SingleOrDefaultAsync(ct);
        if (banned != null)
            throw new InvalidOperationException(
                "Tài khoản người mua đã bị khóa; không thể tạo đơn chờ thanh toán."
            );
        var deadline = Now.AddMinutes(PaymentHoldMinutes);
        var order = await CreateOrderAsync(offer, product, deadline, ct);
        offer.Status = "Accepted";
        offer.OrderId = order.Id;
        turn.Result = "AcceptedUnpaid";
        AddChange(
            offer,
            "Best Offer đã chốt, chờ thanh toán",
            $"Đơn hàng #{order.Id} chờ thanh toán trước "
                + $"{deadline.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} UTC.",
            "orders"
        );
        AddChange(
            offer,
            "Đề nghị đã được chấp nhận",
            $"Đề nghị #{offer.Id} đã được chấp nhận; đơn #{order.Id} đang chờ thanh toán.",
            "best-offers"
        );
        await db.SaveChangesAsync(ct);
        offer.UpdatedAt = Now;
    }

    // Creates an AwaitingPayment order for one unit at the agreed price and holds the stock.
    private async Task<OrderTable> CreateOrderAsync(
        BestOffer offer,
        Product product,
        DateTime? paymentExpiresAt,
        CancellationToken ct
    )
    {
        await finance.ValidateNewSaleAsync(offer.SellerId, ct);
        var buyerAddress = await db.Addresses.SingleOrDefaultAsync(
            x => x.Id == offer.AddressId && x.UserId == offer.BuyerId,
            ct
        ) ?? throw new InvalidOperationException("Địa chỉ giao hàng không còn hợp lệ.");
        var pickup = await db
            .Addresses.Where(x => x.UserId == offer.SellerId && x.IsDefault == true)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(ct)
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
            DeliveryNameSnapshot = buyerAddress.FullName,
            DeliveryStreetSnapshot = buyerAddress.Street,
            DeliveryCitySnapshot = buyerAddress.City,
            DeliveryPostalCodeSnapshot = buyerAddress.PostalCode,
            DeliveryStateSnapshot = buyerAddress.State,
            DeliveryCountrySnapshot = buyerAddress.Country,
            PickupStateSnapshot = pickup.State,
            PickupCountrySnapshot = pickup.Country,
            OrderDate = Now,
            UpdatedAt = Now,
            PaymentExpiresAt = paymentExpiresAt,
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
        return order;
    }

    private async Task<BestOfferView> CloseAsync(
        int actorId,
        long id,
        string role,
        string? key,
        CancellationToken ct
    )
    {
        ValidateKey(key);
        await using var tx = await BeginTransactionAsync(ct);
        var offer = await GetOfferAsync(id, actorId, role, ct);
        if (await IsReplayAsync(offer, actorId, key!, ct))
            return await CommitViewAsync(tx, offer, role, ct);
        RequireStatus(offer, role == "seller" ? PendingSeller : PendingBuyer);
        offer.Status = "Declined";
        offer.UpdatedAt = Now;
        AddTurn(offer, actorId, offer.CurrentAmount, "Decline", key);
        AddChange(
            offer,
            "Đề nghị trả giá đã đóng",
            $"Đề nghị #{offer.Id} đã bị từ chối.",
            "best-offers"
        );
        await db.SaveChangesAsync(ct);
        return await CommitViewAsync(tx, offer, role, ct);
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
        var product = await db
            .Products.AsNoTracking()
            .SingleAsync(x => x.Id == offer.ProductId, ct);
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
            await UsedOffersAsync(offer.BuyerId, offer.ProductId, ct),
            BuyerOfferLimit,
            offer.ExpiresAt,
            offer.OrderId,
            role,
            turns,
            offer.PaymentMethod
        );
    }

    private async Task<BestOfferView> CommitViewAsync(
        IDbContextTransaction? tx,
        BestOffer offer,
        string role,
        CancellationToken ct
    )
    {
        if (tx is not null)
            await tx.CommitAsync(ct);
        return await ViewAsync(offer, role, ct);
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

    // Offers and buyer counteroffers on this product, across every session.
    private Task<int> UsedOffersAsync(int buyerId, int productId, CancellationToken ct) =>
        (
            from turn in db.BestOfferTurns
            join offer in db.BestOffers on turn.BestOfferId equals offer.Id
            where
                offer.BuyerId == buyerId
                && offer.ProductId == productId
                && turn.ActorId == buyerId
                && BuyerOfferActions.Contains(turn.Action)
            select turn.Id
        ).CountAsync(ct);

    private async Task EnsureOfferQuotaAsync(int buyerId, int productId, CancellationToken ct)
    {
        if (await UsedOffersAsync(buyerId, productId, ct) >= BuyerOfferLimit)
            throw new InvalidOperationException(
                $"Bạn đã dùng hết {BuyerOfferLimit} lượt trả giá cho sản phẩm này."
            );
    }

    private async Task<decimal> LastBuyerAmountAsync(BestOffer offer, CancellationToken ct) =>
        await db.BestOfferTurns
            .Where(x =>
                x.BestOfferId == offer.Id
                && x.ActorId == offer.BuyerId
                && BuyerOfferActions.Contains(x.Action)
            )
            .OrderByDescending(x => x.Id)
            .Select(x => x.Amount)
            .FirstAsync(ct);

    // A retried request with the same key returns the stored outcome instead of an error.
    private async Task<bool> IsReplayAsync(
        BestOffer offer,
        int actorId,
        string key,
        CancellationToken ct
    )
    {
        var turn = await db.BestOfferTurns.AsNoTracking().SingleOrDefaultAsync(
            x => x.RequestKey == key,
            ct
        );
        if (turn is null)
            return false;
        if (turn.BestOfferId != offer.Id || turn.ActorId != actorId)
            throw new InvalidOperationException("Khóa thao tác đã được dùng cho yêu cầu khác.");
        return true;
    }

    private BestOfferTurn AddTurn(
        BestOffer offer,
        int actorId,
        decimal amount,
        string action,
        string? key = null
    )
    {
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

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
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

    private static bool IsPayLater(string? method) =>
        method switch
        {
            PayLaterMethod => true,
            AutoPayMethod => false,
            _ => throw new ArgumentException(
                "Chọn cách thanh toán: thanh toán sau hoặc Auto-Pay bằng thẻ."
            ),
        };

    private static string Money(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture) + " USD";

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
