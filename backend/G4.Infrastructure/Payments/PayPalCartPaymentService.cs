using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using G4.Contracts.Checkout;
using G4.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Payments;
public sealed class PayPalCartPaymentService(ApplicationDbContext db, IPayPalBatchGateway gateway, IConfiguration config,
    ISellerFinanceService finance) : IPayPalCartPaymentService
{
    private static string Reference(int orderId) => "order-" + orderId;
    private static string GatewayKey(PayPalCheckout batch, string operation) => "g4-" + operation + "-" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{batch.Id}\n{batch.BuyerId}\n{batch.CheckoutKey}\n{batch.RequestKey}\n{batch.CreatedAt.Ticks}")));
    private static bool InFlight(string status) => status is "Creating" or "Pending" or "Verifying";
    private async Task<CheckoutBatch> SourceAsync(int buyerId, string key, CancellationToken ct) =>
        await db.CheckoutBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Key == key && x.BuyerId == buyerId, ct)
        ?? throw new UnauthorizedAccessException("Không tìm thấy giỏ đã đặt thuộc tài khoản này.");
    private static int[] Ids(CheckoutBatch source)
    {
        var ids = JsonSerializer.Deserialize<int[]>(source.OrderIdsJson) ?? [];
        if (ids.Length is < 1 or > 10 || ids.Any(x => x <= 0) || ids.Distinct().Count() != ids.Length)
            throw new InvalidOperationException("Danh sách đơn của giỏ không hợp lệ.");
        return ids.Order().ToArray();
    }
    private async Task<IDbContextTransaction?> BeginAsync(string checkoutKey, int[] ids, CancellationToken ct)
    {
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction != null) return null;
        var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        try {
            await PromotionLifecycle.LockReservationsAsync(db, ct);
            if (db.Database.IsSqlServer()) {
                var resource = "commerce:paypal-cart:" + checkoutKey;
                await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; IF @r<0 THROW 51000,'PayPal cart is busy',1;", ct);
                foreach (var id in ids) {
                    resource = "commerce:order:" + id;
                    await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; IF @r<0 THROW 51000,'Order is busy',1;", ct);
                    await db.OrderTables.FromSqlInterpolated($"SELECT * FROM [OrderTable] WITH (UPDLOCK,HOLDLOCK) WHERE [id]={id}").AsNoTracking().SingleAsync(ct);
                }
            }
            return tx;
        } catch { await tx.DisposeAsync(); throw; }
    }
    private async Task<PayPalCheckout> OwnedAsync(int buyerId, long id, CancellationToken ct) =>
        await db.PayPalCheckouts.SingleOrDefaultAsync(x => x.Id == id && x.BuyerId == buyerId, ct)
        ?? throw new UnauthorizedAccessException("Không tìm thấy lượt thanh toán của bạn.");
    private async Task ReloadAsync(object entity, CancellationToken ct) { if (db.Database.IsRelational()) await db.Entry(entity).ReloadAsync(ct); }

    public async Task<PayPalCartView> StartAsync(int buyerId, PayPalCartStartInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.CheckoutKey) || input.CheckoutKey.Length > 80 || string.IsNullOrWhiteSpace(input.Key) || input.Key.Length > 100)
            throw new ArgumentException("Cần mã giỏ đã đặt và mã yêu cầu thanh toán.");
        var source = await SourceAsync(buyerId, input.CheckoutKey, ct); var ids = Ids(source);
        PayPalCheckout batch;
        await using (var tx = await BeginAsync(source.Key, ids, ct)) {
            var previous = await db.PayPalCheckouts.SingleOrDefaultAsync(x => x.BuyerId == buyerId && x.RequestKey == input.Key, ct);
            if (previous != null && previous.CheckoutKey != source.Key) throw new InvalidOperationException("Mã thanh toán đã dùng cho giỏ khác.");
            batch = previous ?? await db.PayPalCheckouts.Where(x => x.BuyerId == buyerId && x.CheckoutKey == source.Key
                && (x.Status == "Creating" || x.Status == "Pending" || x.Status == "Verifying")).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct) ?? new PayPalCheckout();
            if (batch.Id > 0) await ReloadAsync(batch, ct);
            if (batch.Id == 0) {
                var orders = await db.OrderTables.Where(o => ids.Contains(o.Id) && o.BuyerId == buyerId).OrderBy(o => o.Id).ToListAsync(ct);
                if (orders.Count != ids.Length) throw new UnauthorizedAccessException("Giỏ chứa đơn không thuộc tài khoản này.");
                foreach (var order in orders) await ReloadAsync(order, ct);
                var payable = orders.Where(o => o.Status == "AwaitingPayment").ToArray();
                if (payable.Length == 0) throw new InvalidOperationException("Giỏ không còn đơn chờ thanh toán.");
                if (payable.Any(o => o.PaymentExpiresAt <= DateTime.UtcNow || o.TotalPrice is null or <= 0)) throw new InvalidOperationException("Đơn đã hết hạn hoặc số tiền không hợp lệ.");
                if (payable.Select(o => o.Currency).Distinct().Count() != 1) throw new InvalidOperationException("Các đơn phải dùng cùng loại tiền tệ.");
                foreach (var order in payable) {
                    if (await db.Payments.AnyAsync(p => p.OrderId == order.Id && (p.Status == "Succeeded" || p.Status == "Verifying"
                        || p.PayPalCheckoutId != null && p.Status == "Pending"), ct)) throw new InvalidOperationException("Một đơn đang có lượt thanh toán chưa kết thúc.");
                    await finance.ValidateMonthlyLimitAsync(order.Id, ct);
                }
                batch = new PayPalCheckout { BuyerId = buyerId, CheckoutKey = source.Key, RequestKey = input.Key, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
                db.PayPalCheckouts.Add(batch); await db.SaveChangesAsync(ct);
                foreach (var order in payable) db.Payments.Add(new Payment { OrderId = order.Id, UserId = buyerId, Amount = order.TotalPrice,
                    Method = "PayPal", Status = "Pending", PayPalCheckoutId = batch.Id, IdempotencyKey = $"paypal-cart:{batch.Id}:{order.Id}", CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync(ct);
            }
            if (tx != null) await tx.CommitAsync(ct);
        }
        if (batch.Status != "Creating") return await ViewAsync(batch, ct);
        // Durable attempt exists before the remote call. Replays use the same provider request ID.
        var payments = await db.Payments.AsNoTracking().Where(p => p.PayPalCheckoutId == batch.Id).OrderBy(p => p.OrderId).ToListAsync(ct);
        var units = payments.Select(p => new PayPalPurchaseUnit(Reference(p.OrderId!.Value), p.Amount!.Value,
            db.OrderTables.AsNoTracking().Where(o => o.Id == p.OrderId).Select(o => o.Currency).Single())).ToArray();
        var frontend = (config["Frontend:PublicBaseUrl"] ?? "http://localhost:5131").TrimEnd('/');
        var created = await gateway.CreateBatchAsync(units, GatewayKey(batch, "create"),
            $"{frontend}/Buyer/PayPalCartReturn?checkoutId={batch.Id}", $"{frontend}/Buyer/PayPalCartCancel?checkoutId={batch.Id}", ct);
        await using (var tx = await BeginAsync(source.Key, ids, ct)) {
            await ReloadAsync(batch, ct);
            if (batch.ProviderOrderId != null && batch.ProviderOrderId != created.Id) throw new InvalidOperationException("PayPal trả về mã giao dịch không nhất quán.");
            batch.ProviderOrderId = created.Id; batch.ApprovalUrl = created.ApprovalUrl;
            if (batch.Status == "Creating") batch.Status = "Pending";
            batch.UpdatedAt = DateTime.UtcNow;
            foreach (var p in await db.Payments.Where(p => p.PayPalCheckoutId == batch.Id).ToListAsync(ct)) { await ReloadAsync(p, ct); p.ProviderOrderId = created.Id; }
            await db.SaveChangesAsync(ct); if (tx != null) await tx.CommitAsync(ct);
        }
        return await ViewAsync(batch, ct);
    }

    public async Task<PayPalCartView> CaptureAsync(int buyerId, long id, string providerOrderId, CancellationToken ct = default)
    {
        var batch = await OwnedAsync(buyerId, id, ct); var source = await SourceAsync(buyerId, batch.CheckoutKey, ct); var ids = Ids(source);
        await using (var tx = await BeginAsync(source.Key, ids, ct)) {
            await ReloadAsync(batch, ct);
            if (batch.ProviderOrderId != providerOrderId || string.IsNullOrWhiteSpace(providerOrderId)) throw new UnauthorizedAccessException("Mã PayPal không thuộc lượt thanh toán này.");
            if (!InFlight(batch.Status)) { if (tx != null) { await tx.CommitAsync(ct); await tx.DisposeAsync(); } return await ViewAsync(batch, ct); }
            var payments = await db.Payments.Where(p => p.PayPalCheckoutId == id).OrderBy(p => p.OrderId).ToListAsync(ct);
            foreach (var p in payments) {
                await ReloadAsync(p, ct);
                if (p.Status == "Succeeded") continue;
                var order = await db.OrderTables.SingleAsync(o => o.Id == p.OrderId, ct); await ReloadAsync(order, ct);
                if (order.Status != "AwaitingPayment" || order.PaymentExpiresAt <= DateTime.UtcNow && batch.Status != "Verifying")
                    throw new InvalidOperationException("Một đơn không còn được thanh toán; chưa gọi thu tiền PayPal.");
                if (await db.Payments.AnyAsync(other => other.OrderId == order.Id && other.Id != p.Id && (other.Status == "Succeeded" || other.Status == "Verifying"), ct))
                    throw new InvalidOperationException("Đơn đang được xử lý bằng một lượt thanh toán khác.");
                if (p.Status != "Verifying") {
                    await finance.ValidateMonthlyLimitAsync(order.Id, ct);
                    p.CaptureReservedAt = DateTime.UtcNow;
                    p.ReservedSellerGross = order.PricingSchemaVersion == 1 ? order.SellerGrossSnapshot : order.TotalPrice;
                }
                p.Status = "Verifying";
            }
            batch.Status = "Verifying"; batch.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct); if (tx != null) await tx.CommitAsync(ct);
        }
        try {
            return await ApplyAsync(batch, await gateway.CaptureBatchAsync(providerOrderId, GatewayKey(batch, "capture"), ct), ct);
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) {
            return await ViewAsync(batch, ct); // Unknown capture remains reserved for reconciliation.
        }
    }

    public async Task<PayPalCartView> ReconcileAsync(int buyerId, long id, CancellationToken ct = default)
    {
        var batch = await OwnedAsync(buyerId, id, ct);
        if (!InFlight(batch.Status) || batch.ProviderOrderId == null) return await ViewAsync(batch, ct);
        var result = await gateway.GetBatchAsync(batch.ProviderOrderId, ct);
        return await ApplyAsync(batch, result, ct);
    }

    private async Task<PayPalCartView> ApplyAsync(PayPalCheckout batch, PayPalBatchCaptured result, CancellationToken ct)
    {
        var source = await SourceAsync(batch.BuyerId, batch.CheckoutKey, ct); var ids = Ids(source);
        await using var tx = await BeginAsync(source.Key, ids, ct); await ReloadAsync(batch, ct);
        var payments = await db.Payments.Where(p => p.PayPalCheckoutId == batch.Id).OrderBy(p => p.OrderId).ToListAsync(ct);
        foreach (var p in payments) await ReloadAsync(p, ct);
        var expected = payments.ToDictionary(p => Reference(p.OrderId!.Value));
        var duplicateCapture = result.Units.Where(u => u.Status == "COMPLETED").GroupBy(u => u.CaptureId).Any(g => g.Count() > 1);
        var invalid = result.ProviderOrderId != batch.ProviderOrderId || result.Units.GroupBy(u => u.ReferenceId).Any(g => g.Count() > 1) || duplicateCapture
            || result.Units.Any(u => !expected.TryGetValue(u.ReferenceId, out var p) || u.Status == "COMPLETED" && (string.IsNullOrWhiteSpace(u.CaptureId)
                || u.Amount != p.Amount || u.Currency != db.OrderTables.AsNoTracking().Where(o => o.Id == p.OrderId).Select(o => o.Currency).Single()));
        foreach (var unit in result.Units.Where(u => u.Status == "COMPLETED" && u.CaptureId != null))
            if (await db.Payments.AnyAsync(p => p.ProviderTransactionId == unit.CaptureId && p.PayPalCheckoutId != batch.Id, ct)) invalid = true;
        if (invalid) {
            batch.Status = "Verifying"; batch.LastError = "PayPal trả về mã đơn, capture, số tiền hoặc tiền tệ không khớp; đang đối soát.";
            foreach (var p in payments.Where(p => p.Status != "Succeeded")) p.Status = "Verifying";
        } else {
            batch.LastError = null;
            foreach (var p in payments) {
                if (p.Status == "Succeeded") {
                    var reported = result.Units.SingleOrDefault(u => u.ReferenceId == Reference(p.OrderId!.Value));
                    if (reported?.Status == "COMPLETED" && reported.CaptureId != p.ProviderTransactionId) throw new InvalidOperationException("Capture ID đã thanh toán bị thay đổi.");
                    continue;
                }
                var unit = result.Units.SingleOrDefault(u => u.ReferenceId == Reference(p.OrderId!.Value));
                var order = await db.OrderTables.SingleAsync(o => o.Id == p.OrderId, ct); await ReloadAsync(order, ct);
                if (unit?.Status == "COMPLETED") {
                    if (order.Status is "Cancelled" or "Expired") { p.Status = "Verifying"; batch.LastError = "Đã thu tiền cho đơn hết hiệu lực; cần đối soát."; continue; }
                    p.Status = "Succeeded"; p.ProviderTransactionId = unit.CaptureId; p.PaidAt = DateTime.UtcNow; p.UpdatedAt = DateTime.UtcNow;
                    await PromotionLifecycle.ConsumeAsync(db, order.Id, ct);
                    if (order.Status == "AwaitingPayment") {
                        order.Status = "Paid"; order.UpdatedAt = DateTime.UtcNow;
                        await new CheckoutService(db, config: config).QueueEmailAsync(order, "PaymentSucceeded", "Thanh toán thành công", $"Đơn hàng #{order.Id} đã thanh toán trong giỏ PayPal.", ct);
                    }
                    await db.SaveChangesAsync(ct); await finance.RecordSuccessfulPaymentAsync(order.Id, p.Id, ct);
                } else if (unit?.Status is "DECLINED" or "FAILED" or "DENIED" || result.Status is "VOIDED" or "DECLINED") p.Status = "Failed";
                else p.Status = batch.Status == "Pending" && result.Status is "CREATED" or "APPROVED" or "PAYER_ACTION_REQUIRED" ? "Pending" : "Verifying";
            }
            batch.Status = payments.All(p => p.Status == "Succeeded") ? "Succeeded"
                : payments.Any(p => p.Status == "Verifying") ? "Verifying"
                : payments.All(p => p.Status == "Pending") ? "Pending"
                : payments.Any(p => p.Status == "Succeeded") ? "PartiallyPaid" : "Failed";
        }
        batch.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct); if (tx != null) { await tx.CommitAsync(ct); await tx.DisposeAsync(); }
        return await ViewAsync(batch, ct);
    }

    public async Task<PayPalCartView> CancelAsync(int buyerId, long id, CancellationToken ct = default)
    {
        var batch = await OwnedAsync(buyerId, id, ct); var source = await SourceAsync(buyerId, batch.CheckoutKey, ct);
        await using var tx = await BeginAsync(source.Key, Ids(source), ct); await ReloadAsync(batch, ct);
        if (batch.Status == "Verifying") throw new InvalidOperationException("PayPal đang xác minh; chưa thể hủy hoặc thanh toán lại.");
        if (batch.Status is "Creating" or "Pending") {
            batch.Status = "Cancelled"; batch.UpdatedAt = DateTime.UtcNow;
            foreach (var p in await db.Payments.Where(p => p.PayPalCheckoutId == id).ToListAsync(ct)) if (p.Status != "Succeeded") p.Status = "Failed";
            await db.SaveChangesAsync(ct);
        }
        if (tx != null) { await tx.CommitAsync(ct); await tx.DisposeAsync(); }
        return await ViewAsync(batch, ct);
    }
    public async Task<PayPalCartView> GetAsync(int buyerId, long id, CancellationToken ct = default) => await ViewAsync(await OwnedAsync(buyerId, id, ct), ct);
    private async Task<PayPalCartView> ViewAsync(PayPalCheckout batch, CancellationToken ct)
    {
        var ids = Ids(await SourceAsync(batch.BuyerId, batch.CheckoutKey, ct));
        var orders = await db.OrderTables.AsNoTracking().Where(o => ids.Contains(o.Id) && o.BuyerId == batch.BuyerId).OrderBy(o => o.Id)
            .Select(o => new PayPalCartOrderView(o.Id, o.Status, o.TotalPrice ?? 0, o.Currency,
                db.Payments.Where(p => p.OrderId == o.Id).OrderByDescending(p => p.Status == "Succeeded").ThenByDescending(p => p.Status == "Verifying").ThenByDescending(p => p.Id).Select(p => p.Status).FirstOrDefault() ?? "Pending")).ToListAsync(ct);
        return new(batch.Id, batch.CheckoutKey, batch.Status, batch.Status == "Pending" ? batch.ApprovalUrl : null, orders, batch.LastError);
    }
    public async Task<int> MaintainAsync(CancellationToken ct = default)
    {
        var pending = await db.PayPalCheckouts.AsNoTracking().Where(x => x.Status == "Verifying").OrderBy(x => x.UpdatedAt).Take(10).ToListAsync(ct);
        foreach (var batch in pending) {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(5));
            try { await ReconcileAsync(batch.BuyerId, batch.Id, budget.Token); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { db.ChangeTracker.Clear(); }
        }
        return pending.Count;
    }
}
