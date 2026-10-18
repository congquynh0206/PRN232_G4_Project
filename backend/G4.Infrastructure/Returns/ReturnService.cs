using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using G4.Infrastructure.Diagnostics;
using G4.Contracts.Checkout;
using G4.Infrastructure.Notifications;
using G4.Infrastructure.Promotions;

namespace G4.Infrastructure.Returns;
public sealed class ReturnService(
    ApplicationDbContext db,
    IRefundGateway refunds,
    ICarrierGateway carrier,
    ISellerFinanceService? finance = null,
    IConfiguration? config = null,
    TimeProvider? clock = null,
    IIntegrationLogWriter? logs = null
) : IReturnService
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    private int ReceiptSeconds => Math.Max(1, config?.GetValue("Returns:ReceiptSeconds", 172800) ?? 172800);

    public Task<ReturnQuote> QuoteAsync(int orderId, string reasonCode, CancellationToken ct = default) =>
        ReturnQuoteBuilder.BuildAsync(db, orderId, reasonCode, Now, ct);

    public async Task<ReturnRequest> RequestPricedReturnAsync(int orderId, ReturnRequestInput input, CancellationToken ct = default)
    {
        _ = ReturnPricing.ReasonLabel(input.ReasonCode);
        if (string.IsNullOrWhiteSpace(input.Key) || input.Key.Length > 100 || string.IsNullOrWhiteSpace(input.PricingFingerprint)
            || input.PricingFingerprint.Length != 64 || (input.Description?.Length ?? 0) > 2000)
            throw new ArgumentException("Yêu cầu cần khóa, báo giá đã xác nhận và mô tả tối đa 2.000 ký tự.");
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await LockOrderAsync(orderId, ct);
        var existing = await db.ReturnRequests.SingleOrDefaultAsync(x => x.OrderId == orderId || x.RequestKey == input.Key, ct);
        if (existing != null)
        {
            if (existing.OrderId != orderId || existing.RequestKey != input.Key || existing.ReasonCode != input.ReasonCode
                || existing.Description != (input.Description ?? "").Trim() || existing.PricingFingerprint != input.PricingFingerprint)
                throw new InvalidOperationException("Đơn hoặc khóa đã có yêu cầu khác. Tải lại chi tiết đơn.");
            return existing;
        }
        var quote = await QuoteAsync(orderId, input.ReasonCode, ct);
        if (!quote.CanRequest) throw new InvalidOperationException(quote.BlockReason);
        if (quote.PricingFingerprint != input.PricingFingerprint)
            throw new InvalidOperationException("Khoản hoàn hoặc phí đã thay đổi. Xem lại báo giá trước khi xác nhận.");
        var order = await db.OrderTables.SingleAsync(x => x.Id == orderId, ct);
        var request = new ReturnRequest
        {
            OrderId = orderId, UserId = order.BuyerId, Reason = quote.ReasonLabel, Description = (input.Description ?? "").Trim(),
            Status = "Requested", CreatedAt = Now, ReturnDeadline = Now.AddDays(7), PricingPolicyVersion = 1,
            ReasonCode = input.ReasonCode, RequestKey = input.Key, PricingFingerprint = quote.PricingFingerprint,
            ShippingPayer = quote.ShippingPayer, GoodsPaidSnapshot = quote.GoodsPaid,
            OutboundShippingRetained = quote.OutboundShippingRetained, ReturnShippingFee = quote.ReturnShippingFee,
            BuyerReturnShippingFee = quote.BuyerReturnShippingFee, BuyerRefundAmount = quote.BuyerRefund,
            PlatformSubsidyReversal = quote.PlatformSubsidyReversal, SellerGrossRefund = quote.SellerGrossRefund,
            WeightKgSnapshot = quote.WeightKg, PickupAddressSnapshot = quote.PickupAddress, DeliveryAddressSnapshot = quote.DeliveryAddress
        };
        db.ReturnRequests.Add(request);
        order.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        await new BusinessNotificationService(db, config, clock).ReturnAsync(request, "ReturnRequested", ct);
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return request;
    }

    public async Task<ReturnRequest> RequestReturnAsync(int orderId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Return reason required");
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await LockOrderAsync(orderId, ct);
        var existing = await db.ReturnRequests.SingleOrDefaultAsync(r => r.OrderId == orderId, ct);
        if (existing is not null)
            return existing;
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        var deliveredAt = await db.ShippingInfos.Where(s => s.OrderId == orderId && s.Direction == "Outbound").Select(s => s.DeliveredAt)
            .SingleOrDefaultAsync(ct);
        var agreedReturn = await db.Disputes.AnyAsync(
            d => d.OrderId == orderId && d.WorkflowEnabled && d.IsOpen && d.Status == "ExecutingAgreement" && d.Proposal == "ReturnRefund",
            ct
        );
        if (!agreedReturn && await db.Disputes.AnyAsync(d => d.OrderId == orderId && d.WorkflowEnabled && d.IsOpen, ct))
            throw new InvalidOperationException("Đơn đang có yêu cầu giải quyết; hãy xử lý trả hàng trong yêu cầu đó.");
        if (!agreedReturn && (order.Status != "Delivered" || deliveredAt is null || deliveredAt < Now.AddDays(-7)))
            throw new InvalidOperationException("Order is outside return window");
        var request = new ReturnRequest
        {
            OrderId = orderId,
            UserId = order.BuyerId,
            Reason = reason,
            Status = "Requested",
            CreatedAt = Now,
            ReturnDeadline = Now.AddDays(7)
        };
        // Accepted full-refund agreements have no buyer postage deduction. Old orders
        // without enough shipping snapshots keep their pre-upgrade treatment.
        if (agreedReturn && ReturnQuoteBuilder.HasRouteSnapshot(order))
        {
            var q = await ReturnQuoteBuilder.BuildAgreementAsync(db, order, ct);
            request.PricingPolicyVersion = 1;
            request.ReasonCode = q.ReasonCode;
            request.RequestKey = $"agreement-return:{orderId}";
            request.PricingFingerprint = q.PricingFingerprint;
            request.ShippingPayer = q.ShippingPayer;
            request.GoodsPaidSnapshot = q.GoodsPaid;
            request.OutboundShippingRetained = q.OutboundShippingRetained;
            request.ReturnShippingFee = q.ReturnShippingFee;
            request.BuyerReturnShippingFee = q.BuyerReturnShippingFee;
            request.BuyerRefundAmount = q.BuyerRefund;
            request.PlatformSubsidyReversal = q.PlatformSubsidyReversal;
            request.SellerGrossRefund = q.SellerGrossRefund;
            request.WeightKgSnapshot = q.WeightKg;
            request.PickupAddressSnapshot = q.PickupAddress;
            request.DeliveryAddressSnapshot = q.DeliveryAddress;
        }
        db.ReturnRequests.Add(request);
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await new BusinessNotificationService(db, config, clock).ReturnAsync(request, "ReturnRequested", ct);
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return request;
    }

    public async Task<ReturnRequest> ApproveAsync(int returnId, CancellationToken ct = default)
    {
        var orderId = await db.ReturnRequests.AsNoTracking().Where(r => r.Id == returnId).Select(r => r.OrderId!.Value).SingleAsync(ct);
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await LockOrderAsync(orderId, ct);
        var request = await db.ReturnRequests.SingleAsync(r => r.Id == returnId, ct);
        await db.Entry(request).ReloadAsync(ct);
        await EnsureRefundAllowedAsync(orderId, ct);
        if (request.Status is "Approved" or "ReturnShipping")
            return request;
        if (request.Status != "Requested")
            throw new InvalidOperationException("Return cannot be approved");
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
        await new BusinessNotificationService(db, config, clock).ReturnAsync(request, "ReturnApproved", ct);
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
        var orderId = await db.ReturnRequests.AsNoTracking().Where(x => x.Id == returnId).Select(x => x.OrderId!.Value).SingleAsync(ct);
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await LockOrderAsync(orderId, ct);
        var request = await db.ReturnRequests.SingleAsync(r => r.Id == returnId, ct);
        await db.Entry(request).ReloadAsync(ct);
        if (request.Status is not ("Approved" or "ReturnShipping"))
            throw new InvalidOperationException("Return must be approved before creating its shipment");
        var shipment = await new ShipmentService(db, carrier).CreateReturnAsync(request.OrderId!.Value, ct);
        if (shipment.TrackingNumber is not null)
        {
            request.Status = "ReturnShipping";
            if (request.PricingPolicyVersion == 1)
            {
                if (finance == null) throw new InvalidOperationException("Return shipping requires finance service");
                await finance.ApplyReturnShippingCostAsync(orderId, returnId, request.ReturnShippingFee, ct);
                request.ShippingCostChargedAt ??= Now;
            }
        }
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return request;
    }

    public async Task<ReturnRequest> RejectAsync(int returnId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Decision reason required");
        var orderId = await db.ReturnRequests.AsNoTracking().Where(x => x.Id == returnId).Select(x => x.OrderId!.Value).SingleAsync(ct);
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await LockOrderAsync(orderId, ct);
        var request = await db.ReturnRequests.SingleAsync(r => r.Id == returnId, ct);
        await db.Entry(request).ReloadAsync(ct);
        if (request.Status != "Requested")
            throw new InvalidOperationException("Return cannot be rejected");
        request.Status = "Rejected";
        request.DecisionReason = reason;
        request.DecidedAt = DateTime.UtcNow;
        (await db.OrderTables.SingleAsync(o => o.Id == request.OrderId, ct)).UpdatedAt = DateTime.UtcNow;
        await new BusinessNotificationService(db, config, clock).ReturnAsync(request, "ReturnRejected", ct);
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return request;
    }

    public async Task<ReturnRequest> MarkReceivedAsync(int returnId, CancellationToken ct = default)
    {
        var orderId = await db.ReturnRequests.AsNoTracking().Where(r => r.Id == returnId).Select(r => r.OrderId!.Value).SingleAsync(ct);
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await LockOrderAsync(orderId, ct);
        var request = await db.ReturnRequests.SingleAsync(r => r.Id == returnId, ct);
        await db.Entry(request).ReloadAsync(ct);
        if (request.Status == "Refunded")
            return request;
        await EnsureRefundAllowedAsync(orderId, ct);
        if (request.Status is not ("Approved" or "ReturnShipping"))
            throw new InvalidOperationException("Return is not in transit");
        var shipment = await db.ShippingInfos.SingleOrDefaultAsync(s => s.OrderId == request.OrderId && s.Direction == "Return", ct);
        if (shipment?.Status != "Delivered")
            throw new InvalidOperationException("Return parcel has not reached seller");
        request.Status = "ReceivedBySeller";
        request.ReceivedAt = Now;
        (await db.OrderTables.SingleAsync(o => o.Id == request.OrderId, ct)).UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await RefundAsync(orderId, "return", request.Id, ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return request;
    }

    public async Task MaintainAsync(CancellationToken ct = default)
    {
        var now = Now;
        var cutoff = now.AddSeconds(-ReceiptSeconds);
        var ids = await db.ReturnRequests.AsNoTracking().Where(r => !db.Disputes.Any(d => d.OrderId == r.OrderId && d.WorkflowEnabled && d.IsOpen) && (r.Status == "ReceivedBySeller" || r.Status == "RefundFailed" || r.Status == "RefundPending" || (r.Status == "Approved" || r.Status == "ReturnShipping") && db.ShippingInfos.Any(s => s.OrderId == r.OrderId && s.Direction == "Return" && s.Status == "Delivered" && s.DeliveredAt != null && (r.ConfirmationDueAt != null ? r.ConfirmationDueAt <= now : s.DeliveredAt <= cutoff))))
            .OrderBy(r => r.Id)
            .Select(r => r.Id)
            .Take(10)
            .ToListAsync(ct);
        foreach (var id in ids)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                var request = await db.ReturnRequests.SingleAsync(r => r.Id == id, budget.Token);
                if (request.Status is "Approved" or "ReturnShipping")
                    await MarkReceivedAsync(id, budget.Token);
                else
                    await RefundAsync(request.OrderId!.Value, "return", id, budget.Token);
            }
            catch (Exception)when (!ct.IsCancellationRequested)
            {
                // Discard changes rolled back by a timeout or a concurrent receipt/issue decision.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task EnsureRefundAllowedAsync(int orderId, CancellationToken ct)
    {
        if (await db.Disputes.AnyAsync(
            d => d.OrderId == orderId && d.WorkflowEnabled && d.IsOpen && !(d.Status == "ExecutingAgreement" && d.Proposal == "ReturnRefund"),
            ct
        ))
            throw new InvalidOperationException("Đơn đang có tranh chấp; khoản hoàn tiền cần chờ xử lý.");
    }

    private async Task LockOrderAsync(int orderId, CancellationToken ct)
    {
        if (db.Database.IsSqlServer())
            await db.OrderTables.FromSqlInterpolated($"SELECT * FROM [OrderTable] WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {orderId}")
                .SingleAsync(ct);
    }

    public async Task<OrderTable> RequestCancelAsync(int orderId, CancellationToken ct = default)
    {
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        if (db.Database.IsRelational()) await db.Entry(order).ReloadAsync(ct);
        if (order.Status is "CancelRequested" or "Cancelled") { if (tx != null) await tx.CommitAsync(ct); return order; }
        var previousStatus = order.Status;
        order.Status = OrderState.Next(order.Status!, "RequestCancel");
        order.UpdatedAt = DateTime.UtcNow;
        order.CancelPreviousStatus = previousStatus;
        order.CancelDecisionReason = null;
        order.CancelRequestNumber++;
        await new BusinessNotificationService(db, config, clock).CancellationAsync(order, false, ct);
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return order;
    }

    public async Task<OrderTable> DecideCancelAsync(
        int orderId,
        bool approve,
        string? reason = null,
        CancellationToken ct = default
    )
    {
        await using var tx = await PromotionLifecycle.LockOrderAsync(db, orderId, ct);
        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        if (db.Database.IsRelational()) await db.Entry(order).ReloadAsync(ct);
        if (!approve && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Cancellation rejection reason required");
        var nextStatus = OrderState.Next(order.Status!, approve ? "AcceptCancel" : "RejectCancel");
        order.Status = approve ? nextStatus : order.CancelPreviousStatus ?? nextStatus;
        order.UpdatedAt = DateTime.UtcNow;
        order.CancelDecisionReason = approve ? null : reason;
        order.CancelPreviousStatus = null;
        await db.SaveChangesAsync(ct);
        if (approve)
            await RefundAsync(orderId, "cancel", null, ct);
        if (tx != null) await tx.CommitAsync(ct);
        return order;
    }

    public async Task<Refund> RefundAsync(
        int orderId,
        string reason,
        int? returnRequestId,
        CancellationToken ct = default
    )
    {
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        if (db.Database.IsSqlServer())
        {
            // All refund entry points lock the same order before contacting the payment provider.
            // This also serializes refunds with different reasons/idempotency keys.
            await db.OrderTables.FromSqlInterpolated($"SELECT * FROM [OrderTable] WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {orderId}")
                .SingleAsync(ct);
        }

        var refund = await RefundCoreAsync(orderId, reason, returnRequestId, ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return refund;
    }

    private async Task<Refund> RefundCoreAsync(int orderId, string reason, int? returnRequestId, CancellationToken ct)
    {
        using var diagnostic = IntegrationContext.ForOrder(orderId);
        if (reason is not ("return" or "cancel" or "delivery-failed" or "dispute"))
            throw new ArgumentException("Invalid refund reason");
        var key = $"refund-{orderId}-{reason}";
        var existing = await db.Refunds.SingleOrDefaultAsync(r => r.IdempotencyKey == key, ct);
        if (existing?.Status == "Succeeded")
        {
            if (returnRequestId is not null)
            {
                var completed = await db.ReturnRequests.SingleAsync(r => r.Id == returnRequestId && r.OrderId == orderId, ct);
                completed.Status = "Refunded";
                completed.RefundId = existing.Id;
                await db.SaveChangesAsync(ct);
            }

            if (finance is not null)
                await finance.ApplyRefundAsync(orderId, existing.Id, ct);
            return existing;
        }

        var order = await db.OrderTables.SingleAsync(o => o.Id == orderId, ct);
        var decision = reason == "dispute" ? await db.DisputeDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == orderId
            && x.Outcome != "SellerWins" && db.Disputes.Any(d => d.Id == x.DisputeId && d.IsOpen && d.Status == "ExecutingAgreement"), ct) : null;
        ReturnRequest? pricedReturn = null;
        if (reason == "return")
        {
            await EnsureRefundAllowedAsync(orderId, ct);
            var request = await db.ReturnRequests.SingleAsync(r => r.Id == returnRequestId && r.OrderId == orderId, ct);
            if (request.Status is not ("ReceivedBySeller" or "RefundFailed" or "RefundPending"))
                throw new InvalidOperationException("Return has not been received");
            request.Status = "RefundPending";
            if (request.PricingPolicyVersion == 1) pricedReturn = request;
        }

        if (reason == "cancel" && order.Status != "Cancelled")
            throw new InvalidOperationException("Order is not cancelled");
        if (reason == "delivery-failed")
        {
            var shipment = await db.ShippingInfos.SingleAsync(s => s.OrderId == orderId && s.Direction == "Outbound", ct);
            if (shipment.Status != "ReturnedToSeller")
                throw new InvalidOperationException("Shipment has not returned to seller");
        }

        if (reason == "dispute" && !await db.SellerSettlements.AnyAsync(s => s.OrderId == orderId && s.Status == "RefundPending", ct))
            throw new InvalidOperationException("Dispute has not been resolved for the buyer");
        if (reason == "dispute")
            pricedReturn = await db.ReturnRequests.SingleOrDefaultAsync(r => r.OrderId == orderId && r.PricingPolicyVersion == 1
                && r.Status == "Disputed" && r.ShippingCostChargedAt != null, ct);
        var payment = await db.Payments.SingleAsync(p => p.OrderId == orderId && p.Status == "Succeeded", ct);
        var paid = payment.Amount ?? 0m;
        var alreadyRefunded = await db.Refunds.Where(r => r.OrderId == orderId && r.Status == "Succeeded").SumAsync(r => r.Amount, ct);
        var promotionZeroPayment = paid == 0m && payment.Amount == 0m && payment.Method == "Promotion" && order.PricingSchemaVersion == 1 && order.TotalPrice == 0m;
        if (paid < 0 || paid == 0m && !promotionZeroPayment || alreadyRefunded > paid || paid > 0 && paid == alreadyRefunded || await db.Refunds.AnyAsync(r => r.OrderId == orderId && r.Status == "Succeeded" && r.Reason != "shipping-adjustment", ct) || promotionZeroPayment && await db.Refunds.AnyAsync(r => r.OrderId == orderId && r.Status == "Succeeded", ct))
            throw new InvalidOperationException("Refund would exceed captured amount");
        var amount = decision?.BuyerRefund ?? (pricedReturn != null && reason == "return" ? pricedReturn.BuyerRefundAmount : paid - alreadyRefunded);
        if (amount < 0 || amount > paid - alreadyRefunded)
            throw new InvalidOperationException("Khoản hoàn vượt số tiền buyer còn có thể nhận.");
        if (pricedReturn != null && pricedReturn.ShippingCostChargedAt == null)
            throw new InvalidOperationException("Phí vận chuyển trả hàng chưa được ghi nhận.");
        var refund = existing ?? new Refund
        {
            OrderId = orderId,
            PaymentId = payment.Id,
            ReturnRequestId = pricedReturn?.Id ?? returnRequestId,
            DisputeDecisionId = decision?.Id,
            Amount = amount,
            Currency = order.Currency,
            Reason = reason,
            IdempotencyKey = key,
            CreatedAt = DateTime.UtcNow
        };
        if (existing == null && decision != null)
        {
            refund.ReturnPolicyVersion = 1;
            refund.SellerGrossRefundAmount = decision.SellerGrossRefund;
            refund.PlatformSubsidyReversalAmount = decision.SubsidyReversal;
            refund.BuyerReturnShippingRecovery = decision.BuyerShippingRecovery;
        }
        else if (existing == null && pricedReturn != null)
        {
            refund.ReturnPolicyVersion = 1;
            var reversed = -await db.FinancialTransactions.Where(x => x.OrderId == orderId && x.Type == "PlatformSubsidyReversal").SumAsync(x => x.Amount, ct);
            refund.PlatformSubsidyReversalAmount = reason == "return" ? pricedReturn.PlatformSubsidyReversal
                : Math.Max(0m, (order.PricingSchemaVersion == 1 ? order.PlatformSubsidy : 0m) - reversed);
            refund.BuyerReturnShippingRecovery = reason == "return" ? pricedReturn.BuyerReturnShippingFee : 0m;
            refund.SellerGrossRefundAmount = refund.Amount + refund.PlatformSubsidyReversalAmount + refund.BuyerReturnShippingRecovery;
        }
        if (existing is null)
            db.Refunds.Add(refund);
        refund.Status = "Pending";
        await db.SaveChangesAsync(ct);
        try
        {
            refund.ProviderRefundId = refund.Amount == 0 ? $"INTERNAL-{key}" : await refunds.RefundAsync(payment, refund.Amount, key, ct);
            if (refund.Amount == 0)
            {
                await using var internalRefund = new IntegrationCallRecorder(logs).Start(pricedReturn == null ? "Promotion" : "Return", "Refund", "Internal");
                await internalRefund.CompleteAsync("Succeeded", providerReference: refund.ProviderRefundId, ct: ct);
            }

            refund.Status = "Succeeded";
            refund.CompletedAt = DateTime.UtcNow;
            if (reason == "return")
            {
                var request = await db.ReturnRequests.SingleAsync(r => r.Id == returnRequestId, ct);
                request.Status = "Refunded";
                request.RefundId = refund.Id;
            }

            if (reason == "dispute")
            {
                var returned = await db.ReturnRequests.SingleOrDefaultAsync(r => r.OrderId == orderId && r.Status == "Disputed", ct);
                if (returned is not null)
                {
                    returned.Status = "Refunded";
                    returned.RefundId = refund.Id;
                }
            }

            if (reason != "cancel")
                order.Status = "Closed";
            order.UpdatedAt = DateTime.UtcNow;
            await new CheckoutService(db, config: config).QueueEmailAsync(order, "Refunded", "Hoàn tiền thành công", $"Đơn hàng #{orderId} đã được hoàn tiền.", ct);
        }
        catch (Exception ex)when (ex is HttpRequestException or TaskCanceledException)
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
