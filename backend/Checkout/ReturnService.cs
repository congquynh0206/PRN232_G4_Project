using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Checkout;

public sealed class ReturnService(ApplicationDbContext db, IRefundGateway refunds, ICarrierGateway carrier)
{
    public async Task<ReturnRequest> RequestReturnAsync(int orderId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Return reason required");
        var existing = await db.ReturnRequests.SingleOrDefaultAsync(r => r.OrderId == orderId, ct);
        if (existing is not null) return existing;
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        var deliveredAt = await db.ShippingInfos.Where(s => s.OrderId == orderId && s.Direction == "Outbound")
            .Select(s => s.DeliveredAt).SingleOrDefaultAsync(ct);
        if (order.Status != "Delivered" || deliveredAt is null || deliveredAt < DateTime.UtcNow.AddDays(-7))
            throw new InvalidOperationException("Order is outside return window");
        var request = new ReturnRequest
        {
            OrderId = orderId, UserId = order.BuyerId, Reason = reason,
            Status = "Requested", CreatedAt = DateTime.UtcNow,
            ReturnDeadline = DateTime.UtcNow.AddDays(7)
        };
        db.ReturnRequests.Add(request);
        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<ReturnRequest> ApproveAsync(int returnId, CancellationToken ct = default)
    {
        var request = await db.ReturnRequests.SingleAsync(r => r.Id == returnId, ct);
        if (request.Status != "Requested") throw new InvalidOperationException("Return cannot be approved");
        request.Status = "Approved";
        request.DecidedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
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
        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<ReturnRequest> MarkReceivedAsync(int returnId, CancellationToken ct = default)
    {
        var request = await db.ReturnRequests.SingleAsync(r => r.Id == returnId, ct);
        if (request.Status is not ("Approved" or "ReturnShipping"))
            throw new InvalidOperationException("Return is not in transit");
        var shipment = await db.ShippingInfos.SingleOrDefaultAsync(s => s.OrderId == request.OrderId && s.Direction == "Return", ct);
        if (shipment?.Status != "Delivered") throw new InvalidOperationException("Return parcel has not reached seller");
        request.Status = "ReceivedBySeller"; request.ReceivedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<OrderTable> RequestCancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        var previousStatus = order.Status;
        order.Status = OrderState.Next(order.Status!, "RequestCancel");
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
        order.CancelDecisionReason = approve ? null : reason;
        order.CancelPreviousStatus = null;
        await db.SaveChangesAsync(ct);
        if (approve) await RefundAsync(orderId, "cancel", null, ct);
        return order;
    }

    public async Task<Refund> RefundAsync(int orderId, string reason, int? returnRequestId,
        CancellationToken ct = default)
    {
        if (reason is not ("return" or "cancel" or "delivery-failed"))
            throw new ArgumentException("Invalid refund reason");
        var key = $"refund-{orderId}-{reason}";
        var existing = await db.Refunds.SingleOrDefaultAsync(r => r.IdempotencyKey == key, ct);
        if (existing?.Status == "Succeeded") return existing;
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        if (reason == "return")
        {
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
        var payment = await db.Payments.SingleAsync(p => p.OrderId == orderId && p.Status == "Succeeded", ct);
        var paid = payment.Amount ?? 0m;
        var alreadyRefunded = await db.Refunds.Where(r => r.OrderId == orderId && r.Status == "Succeeded")
            .SumAsync(r => r.Amount, ct);
        if (paid <= 0 || paid - alreadyRefunded < paid) throw new InvalidOperationException("Refund would exceed captured amount");
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
            refund.ProviderRefundId = await refunds.RefundAsync(payment, refund.Amount, key, ct);
            refund.Status = "Succeeded";
            refund.CompletedAt = DateTime.UtcNow;
            if (reason == "return")
            {
                var request = await db.ReturnRequests.SingleAsync(r => r.Id == returnRequestId, ct);
                request.Status = "Refunded"; request.RefundId = refund.Id;
            }
            if (reason != "cancel") order.Status = "Closed";
            await new MvpService(db).QueueEmailAsync(order, "Refunded", "Refund completed", $"Order {orderId} was refunded.", ct);
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
        return refund;
    }
}
