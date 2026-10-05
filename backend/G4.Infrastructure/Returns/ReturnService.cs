using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Returns;

public sealed class ReturnService(ApplicationDbContext db, IRefundGateway refunds, ICarrierGateway carrier,
    ISellerFinanceService? finance = null, IConfiguration? config = null, TimeProvider? clock = null) : IReturnService
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    private int ReceiptSeconds => Math.Max(1, config?.GetValue("Returns:ReceiptSeconds", 172800) ?? 172800);

    public async Task<ReturnRequest> RequestReturnAsync(int orderId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Return reason required");
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await LockOrderAsync(orderId, ct);
        var existing = await db.ReturnRequests.SingleOrDefaultAsync(r => r.OrderId == orderId, ct);
        if (existing is not null) return existing;
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        var deliveredAt = await db.ShippingInfos.Where(s => s.OrderId == orderId && s.Direction == "Outbound")
            .Select(s => s.DeliveredAt).SingleOrDefaultAsync(ct);
        var agreedReturn = await db.Disputes.AnyAsync(d => d.OrderId == orderId && d.WorkflowEnabled && d.IsOpen &&
            d.Status == "ExecutingAgreement" && d.Proposal == "ReturnRefund", ct);
        if (!agreedReturn && await db.Disputes.AnyAsync(d => d.OrderId == orderId && d.WorkflowEnabled && d.IsOpen, ct))
            throw new InvalidOperationException("Đơn đang có yêu cầu giải quyết; hãy xử lý trả hàng trong yêu cầu đó.");
        if (!agreedReturn && (order.Status != "Delivered" || deliveredAt is null || deliveredAt < Now.AddDays(-7)))
            throw new InvalidOperationException("Order is outside return window");
        var request = new ReturnRequest
        {
            OrderId = orderId, UserId = order.BuyerId, Reason = reason,
            Status = "Requested", CreatedAt = Now,
            ReturnDeadline = Now.AddDays(7)
        };
        db.ReturnRequests.Add(request);
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return request;
    }

    public async Task<ReturnRequest> ApproveAsync(int returnId, CancellationToken ct = default)
    {
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var orderId = await db.ReturnRequests.Where(r => r.Id == returnId).Select(r => r.OrderId!.Value).SingleAsync(ct);
        await LockOrderAsync(orderId, ct);
        var request = await db.ReturnRequests.SingleAsync(r => r.Id == returnId, ct);
        await db.Entry(request).ReloadAsync(ct);
        await EnsureRefundAllowedAsync(orderId, ct);
        if (request.Status is "Approved" or "ReturnShipping") return request;
        if (request.Status != "Requested") throw new InvalidOperationException("Return cannot be approved");
        if (finance is not null)
        {
            var payment = await db.Payments.SingleAsync(x => x.OrderId == request.OrderId && x.Status == "Succeeded", ct);
            var settlement = await finance.RecordSuccessfulPaymentAsync(orderId, payment.Id, ct);
            if (settlement.Status != "OnHold")
                await finance.PlaceHoldAsync(orderId, $"Trả hàng #{request.Id} đã được duyệt", ct);
        }
        request.Status = "Approved";
        request.DecidedAt = DateTime.UtcNow;
        (await db.OrderTables.SingleAsync(o => o.Id == request.OrderId, ct)).UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
            await transaction.DisposeAsync();
        }
        return await RetryShipmentAsync(returnId, ct);
    }

    public async Task<ReturnRequest> RetryShipmentAsync(int returnId, CancellationToken ct = default)
    {
        var request = await db.ReturnRequests.SingleAsync(r => r.Id == returnId, ct);
        if (request.Status is not ("Approved" or "ReturnShipping"))
            throw new InvalidOperationException("Return must be approved before creating its shipment");
        var shipment = await new ShipmentService(db, carrier).CreateReturnAsync(request.OrderId!.Value, ct);
        if (shipment.TrackingNumber is not null) request.Status = "ReturnShipping";
        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<ReturnRequest> RejectAsync(int returnId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Decision reason required");
        var request = await db.ReturnRequests.SingleAsync(r => r.Id == returnId, ct);
        if (request.Status != "Requested") throw new InvalidOperationException("Return cannot be rejected");
        request.Status = "Rejected"; request.DecisionReason = reason; request.DecidedAt = DateTime.UtcNow;
        (await db.OrderTables.SingleAsync(o => o.Id == request.OrderId, ct)).UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<ReturnRequest> MarkReceivedAsync(int returnId, CancellationToken ct = default)
    {
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var orderId = await db.ReturnRequests.Where(r => r.Id == returnId).Select(r => r.OrderId!.Value).SingleAsync(ct);
        await LockOrderAsync(orderId, ct);
        var request = await db.ReturnRequests.SingleAsync(r => r.Id == returnId, ct);
        await db.Entry(request).ReloadAsync(ct);
        if (request.Status == "Refunded") return request;
        await EnsureRefundAllowedAsync(orderId, ct);
        if (request.Status is not ("Approved" or "ReturnShipping"))
            throw new InvalidOperationException("Return is not in transit");
        var shipment = await db.ShippingInfos.SingleOrDefaultAsync(s => s.OrderId == request.OrderId && s.Direction == "Return", ct);
        if (shipment?.Status != "Delivered") throw new InvalidOperationException("Return parcel has not reached seller");
        request.Status = "ReceivedBySeller"; request.ReceivedAt = Now;
        (await db.OrderTables.SingleAsync(o => o.Id == request.OrderId, ct)).UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await RefundAsync(orderId, "return", request.Id, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return request;
    }

    public async Task MaintainAsync(CancellationToken ct = default)
    {
        var now = Now;
        var cutoff = now.AddSeconds(-ReceiptSeconds);
        var ids = await db.ReturnRequests.AsNoTracking().Where(r =>
            !db.Disputes.Any(d => d.OrderId == r.OrderId && d.WorkflowEnabled && d.IsOpen) &&
            (r.Status == "ReceivedBySeller" || r.Status == "RefundFailed" || r.Status == "RefundPending" ||
             (r.Status == "Approved" || r.Status == "ReturnShipping") &&
             db.ShippingInfos.Any(s => s.OrderId == r.OrderId && s.Direction == "Return" && s.Status == "Delivered" &&
                 s.DeliveredAt != null && (r.ConfirmationDueAt != null ? r.ConfirmationDueAt <= now : s.DeliveredAt <= cutoff))))
            .OrderBy(r => r.Id).Select(r => r.Id).Take(10).ToListAsync(ct);
        foreach (var id in ids)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                var request = await db.ReturnRequests.SingleAsync(r => r.Id == id, budget.Token);
                if (request.Status is "Approved" or "ReturnShipping") await MarkReceivedAsync(id, budget.Token);
                else await RefundAsync(request.OrderId!.Value, "return", id, budget.Token);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Discard changes rolled back by a timeout or a concurrent receipt/issue decision.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task EnsureRefundAllowedAsync(int orderId, CancellationToken ct)
    {
        if (await db.Disputes.AnyAsync(d => d.OrderId == orderId && d.WorkflowEnabled && d.IsOpen &&
            !(d.Status == "ExecutingAgreement" && d.Proposal == "ReturnRefund"), ct))
            throw new InvalidOperationException("Đơn đang có tranh chấp; khoản hoàn tiền cần chờ xử lý.");
    }

    private async Task LockOrderAsync(int orderId, CancellationToken ct)
    {
        if (db.Database.IsSqlServer())
            await db.OrderTables.FromSqlInterpolated(
                $"SELECT * FROM [OrderTable] WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {orderId}").SingleAsync(ct);
    }

    public async Task<OrderTable> RequestCancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        var previousStatus = order.Status;
        order.Status = OrderState.Next(order.Status!, "RequestCancel");
        order.UpdatedAt = DateTime.UtcNow;
        order.CancelPreviousStatus = previousStatus;
        order.CancelDecisionReason = null;
        await db.SaveChangesAsync(ct);
        return order;
    }

    public async Task<OrderTable> DecideCancelAsync(int orderId, bool approve, string? reason = null, CancellationToken ct = default)
    {
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        if (!approve && string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Cancellation rejection reason required");
        var nextStatus = OrderState.Next(order.Status!, approve ? "AcceptCancel" : "RejectCancel");
        order.Status = approve ? nextStatus : order.CancelPreviousStatus ?? nextStatus;
        order.UpdatedAt = DateTime.UtcNow;
        order.CancelDecisionReason = approve ? null : reason;
        order.CancelPreviousStatus = null;
        await db.SaveChangesAsync(ct);
        if (approve) await RefundAsync(orderId, "cancel", null, ct);
        return order;
    }

    public async Task<Refund> RefundAsync(int orderId, string reason, int? returnRequestId,
        CancellationToken ct = default)
    {
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        if (db.Database.IsSqlServer())
        {
            // All refund entry points lock the same order before contacting the payment provider.
            // This also serializes refunds with different reasons/idempotency keys.
            await db.OrderTables.FromSqlInterpolated(
                $"SELECT * FROM [OrderTable] WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {orderId}").SingleAsync(ct);
        }
        var refund = await RefundCoreAsync(orderId, reason, returnRequestId, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return refund;
    }

    private async Task<Refund> RefundCoreAsync(int orderId, string reason, int? returnRequestId,
        CancellationToken ct)
    {
        if (reason is not ("return" or "cancel" or "delivery-failed" or "dispute"))
            throw new ArgumentException("Invalid refund reason");
        var key = $"refund-{orderId}-{reason}";
        var existing = await db.Refunds.SingleOrDefaultAsync(r => r.IdempotencyKey == key, ct);
        if (existing?.Status == "Succeeded")
        {
            if (returnRequestId is not null)
            {
                var completed = await db.ReturnRequests.SingleAsync(r => r.Id == returnRequestId && r.OrderId == orderId, ct);
                completed.Status = "Refunded"; completed.RefundId = existing.Id;
                await db.SaveChangesAsync(ct);
            }
            if (finance is not null) await finance.ApplyRefundAsync(orderId, existing.Id, ct);
            return existing;
        }
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        if (reason == "return")
        {
            await EnsureRefundAllowedAsync(orderId, ct);
            var request = await db.ReturnRequests.SingleAsync(r => r.Id == returnRequestId && r.OrderId == orderId, ct);
            if (request.Status is not ("ReceivedBySeller" or "RefundFailed" or "RefundPending"))
                throw new InvalidOperationException("Return has not been received");
            request.Status = "RefundPending";
        }
        if (reason == "cancel" && order.Status != "Cancelled") throw new InvalidOperationException("Order is not cancelled");
        if (reason == "delivery-failed")
        {
            var shipment = await db.ShippingInfos.SingleAsync(s => s.OrderId == orderId && s.Direction == "Outbound", ct);
            if (shipment.Status != "ReturnedToSeller") throw new InvalidOperationException("Shipment has not returned to seller");
        }
        if (reason == "dispute" && !await db.SellerSettlements.AnyAsync(s =>
                s.OrderId == orderId && s.Status == "RefundPending", ct))
            throw new InvalidOperationException("Dispute has not been resolved for the buyer");
        var payment = await db.Payments.SingleAsync(p => p.OrderId == orderId && p.Status == "Succeeded", ct);
        var paid = payment.Amount ?? 0m;
        var alreadyRefunded = await db.Refunds.Where(r => r.OrderId == orderId && r.Status == "Succeeded")
            .SumAsync(r => r.Amount, ct);
        var promotionZeroPayment = paid == 0m && payment.Amount == 0m && payment.Method == "Promotion" &&
            order.PricingSchemaVersion == 1 && order.TotalPrice == 0m;
        if (paid < 0 || paid == 0m && !promotionZeroPayment || paid - alreadyRefunded < paid ||
            promotionZeroPayment && await db.Refunds.AnyAsync(r => r.OrderId == orderId && r.Status == "Succeeded", ct))
            throw new InvalidOperationException("Refund would exceed captured amount");
        var refund = existing ?? new Refund
        {
            OrderId = orderId, PaymentId = payment.Id, ReturnRequestId = returnRequestId,
            Amount = paid, Currency = order.Currency, Reason = reason, IdempotencyKey = key,
            CreatedAt = DateTime.UtcNow
        };
        if (existing is null) db.Refunds.Add(refund);
        refund.Status = "Pending";
        await db.SaveChangesAsync(ct);
        try
        {
            refund.ProviderRefundId = promotionZeroPayment ? $"INTERNAL-{key}" :
                await refunds.RefundAsync(payment, refund.Amount, key, ct);
            refund.Status = "Succeeded";
            refund.CompletedAt = DateTime.UtcNow;
            if (reason == "return")
            {
                var request = await db.ReturnRequests.SingleAsync(r => r.Id == returnRequestId, ct);
                request.Status = "Refunded"; request.RefundId = refund.Id;
            }
            if (reason == "dispute")
            {
                var returned = await db.ReturnRequests.SingleOrDefaultAsync(r => r.OrderId == orderId && r.Status == "Disputed", ct);
                if (returned is not null) { returned.Status = "Refunded"; returned.RefundId = refund.Id; }
            }
            if (reason != "cancel") order.Status = "Closed";
            order.UpdatedAt = DateTime.UtcNow;
            await new CheckoutService(db).QueueEmailAsync(order, "Refunded", "Hoàn tiền thành công", $"Đơn hàng #{orderId} đã được hoàn tiền.", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            refund.Status = "Failed";
            if (reason == "return")
            {
                var request = await db.ReturnRequests.SingleAsync(r => r.Id == returnRequestId, ct);
                request.Status = "RefundFailed";
            }
        }
        await db.SaveChangesAsync(ct);
        if (refund.Status == "Succeeded" && finance is not null)
            await finance.ApplyRefundAsync(orderId, refund.Id, ct);
        return refund;
    }
}
