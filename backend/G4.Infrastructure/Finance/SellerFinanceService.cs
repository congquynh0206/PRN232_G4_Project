using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Finance;
public sealed partial class SellerFinanceService(ApplicationDbContext db, IConfiguration configuration, TimeProvider? clock = null) : ISellerFinanceService
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async Task ValidateMonthlyLimitAsync(int orderId, CancellationToken ct = default)
    {
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await LockOrderAsync(orderId, ct);
        var order = await db.OrderTables.AsNoTracking().SingleAsync(x => x.Id == orderId, ct);
        var sellerId = order.SellerId ?? throw new InvalidOperationException("Order has no seller");
        await AccountGateAsync(sellerId, ct);
        var account = await GetOrCreateAccountAsync(sellerId, ct);
        await LockAccountAsync(account, ct);
        ResetMonth(account);
        if (account.Status != "Active")
            throw new InvalidOperationException("Seller account is restricted");
        if (account.PermanentSalesBlockedAt != null && order.OrderDate >= account.PermanentSalesBlockedAt)
            throw new InvalidOperationException("Seller is permanently restricted from new sales");
        if (IsDebtOverdue(account) && order.OrderDate >= account.DebtDueAt)
            throw new InvalidOperationException("Seller has overdue debt and cannot receive new orders");
        var reserved = await db.Payments.Where(p => p.OrderId != orderId && p.Status == "Verifying" && p.CaptureReservedAt >= MonthStart()
            && db.OrderTables.Any(o => o.Id == p.OrderId && o.SellerId == sellerId)).SumAsync(p => p.ReservedSellerGross ?? 0m, ct);
        if (account.MonthlySalesAmount + reserved + SnapshotGross(order, order.TotalPrice ?? 0m) > account.MonthlySalesLimit)
            throw new InvalidOperationException($"Seller level {account.Level} monthly sales limit would be exceeded");
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
    }

    public async Task<SellerSettlement> RecordSuccessfulPaymentAsync(int orderId, int paymentId, CancellationToken ct = default)
    {
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await LockOrderAsync(orderId, ct);
        var order = await db.OrderTables.AsNoTracking().SingleAsync(x => x.Id == orderId, ct);
        var sellerId = order.SellerId ?? throw new InvalidOperationException("Order has no seller");
        await AccountGateAsync(sellerId, ct);
        var existing = await db.SellerSettlements.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (existing is not null)
            return existing;
        var payment = await db.Payments.AsNoTracking().SingleAsync(x => x.Id == paymentId, ct);
        if (payment.Status != "Succeeded" || payment.OrderId != orderId)
            throw new InvalidOperationException("Only the order's successful payment can be settled");
        var gross = SnapshotGross(order, payment.Amount ?? order.TotalPrice ?? 0m);
        var paidAt = payment.PaidAt ?? Now;
        var policy = await new PlatformFeeService(db, clock).PolicyAtAsync(paidAt, ct);
        var feePercent = policy?.FeePercent ?? 12m;
        var fixedFee = policy?.FixedFee ?? .30m;
        var fee = gross == 0m && order.PricingSchemaVersion == 1 ? new FeeBreakdown(0m, 0m, 0m, 0m) : SellerFinanceRules.CalculateFees(gross, feePercent, fixedFee);
        var account = await GetOrCreateAccountAsync(sellerId, ct);
        await LockAccountAsync(account, ct);
        ResetMonth(account);
        var authorizedReservation = payment.PayPalCheckoutId != null && payment.CaptureReservedAt != null && payment.ReservedSellerGross == gross;
        if (!authorizedReservation && account.MonthlySalesAmount + gross > account.MonthlySalesLimit)
            throw new InvalidOperationException($"Seller level {account.Level} monthly sales limit exceeded");
        var now = Now;
        var settlement = new SellerSettlement
        {
            SellerAccountId = account.Id,
            SellerId = sellerId,
            OrderId = orderId,
            PaymentId = paymentId,
            GrossAmount = gross,
            PlatformFeeAmount = fee.TotalFee,
            FixedFeeAmount = fee.FixedFee,
            NetAmount = fee.NetAmount,
            ProcessingAmount = fee.NetAmount,
            Currency = order.Currency,
            Status = "Processing",
            CreatedAt = now,
            HoldPolicyVersion = 1,
            SellerLevelSnapshot = account.Level,
            HoldSecondsSnapshot = HoldSeconds(account.Level, account.HoldDays),
            HoldStartedAt = account.Level == 3 ? paidAt : null,
            ReleaseAt = account.Level == 3 ? paidAt : null,
            FeePercentSnapshot = feePercent,
            FeePolicyId = policy?.Id
        };
        await InitializeDeliveryScheduleAsync(settlement, ct);
        db.SellerSettlements.Add(settlement);
        await db.SaveChangesAsync(ct);
        var recovered = Math.Min(account.NegativeBalance, settlement.ProcessingAmount);
        account.NegativeBalance -= recovered;
        settlement.ProcessingAmount -= recovered;
        account.ProcessingBalance += settlement.ProcessingAmount;
        account.MonthlySalesAmount += gross;
        account.UpdatedAt = now;
        var subsidy = order.PricingSchemaVersion == 1 ? order.PlatformSubsidy : 0m;
        AddEntry(
            account,
            settlement,
            "Sale",
            "Processing",
            gross - subsidy,
            $"order:{orderId}:sale",
            "Buyer payment received"
        );
        if (subsidy > 0)
            AddEntry(
                account,
                settlement,
                "PlatformSubsidy",
                "Processing",
                subsidy,
                $"order:{orderId}:platform-subsidy",
                "Platform promotion funded seller proceeds"
            );
        AddEntry(
            account,
            settlement,
            "PlatformFee",
            "Processing",
            -fee.TotalFee,
            $"order:{orderId}:fee",
            $"Platform fee {feePercent:0.##}% + {fee.FixedFee:0.00}"
        );
        if (recovered > 0)
            AddEntry(
                account,
                settlement,
                "DebtRecovery",
                "Negative",
                -recovered,
                $"order:{orderId}:debt-recovery",
                "Previous negative balance recovered from sale proceeds"
            );
        if (settlement.SellerLevelSnapshot == 3)
            ReleaseToWallet(settlement, account, "Instant seller proceeds credited after confirmed payment");
        await SyncDebtAsync(account, ct);
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return settlement;
    }

    public async Task ApplyRefundAsync(int orderId, int refundId, CancellationToken ct = default)
    {
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await LockOrderAsync(orderId, ct);
        var order = await db.OrderTables.AsNoTracking().SingleAsync(x => x.Id == orderId, ct);
        await AccountGateAsync(order.SellerId ?? throw new InvalidOperationException("Order has no seller"), ct);
        var marker = $"refund:{refundId}:applied";
        if (await db.FinancialTransactions.AnyAsync(x => x.EntryKey == marker, ct))
            return;
        var refund = await db.Refunds.AsNoTracking().SingleAsync(x => x.Id == refundId, ct);
        if (refund.Status != "Succeeded" || refund.OrderId != orderId || refund.Amount < 0)
            throw new InvalidOperationException("Only the order's successful refund affects seller balance");
        var settlement = await db.SellerSettlements.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (settlement is null)
        {
            var paymentId = await db.Payments.Where(x => x.OrderId == orderId && x.Status == "Succeeded").Select(x => x.Id)
                .FirstAsync(ct);
            settlement = await RecordSuccessfulPaymentAsync(orderId, paymentId, ct);
        }

        await db.Entry(settlement).ReloadAsync(ct);
        if (refund.PaymentId != settlement.PaymentId)
            throw new InvalidOperationException("Refund belongs to another payment");
        var account = await db.SellerAccounts.SingleAsync(x => x.Id == settlement.SellerAccountId, ct);
        await LockAccountAsync(account, ct);
        var subsidyReversal = 0m;
        var buyerShippingRecovery = refund.ReturnPolicyVersion == 1 ? refund.BuyerReturnShippingRecovery : 0m;
        if (refund.ReturnPolicyVersion == 1)
        {
            var buyerPaid = (await db.Payments.AsNoTracking().SingleAsync(x => x.Id == settlement.PaymentId, ct)).Amount ?? 0m;
            var totalBuyerRefund = await db.Refunds.Where(x => x.OrderId == orderId && x.Status == "Succeeded").SumAsync(x => x.Amount, ct);
            if (totalBuyerRefund > buyerPaid) throw new InvalidOperationException("Refund would exceed captured buyer payment");
            var reversed = -await db.FinancialTransactions.Where(x => x.OrderId == orderId && x.Type == "PlatformSubsidyReversal").SumAsync(x => x.Amount, ct);
            subsidyReversal = refund.PlatformSubsidyReversalAmount;
            var subsidyRemaining = (order.PricingSchemaVersion == 1 ? order.PlatformSubsidy : 0m) - reversed;
            if (subsidyReversal < 0 || subsidyReversal > subsidyRemaining || buyerShippingRecovery < 0
                || buyerShippingRecovery > settlement.ReturnShippingCost - settlement.ReturnShippingRecovery
                || refund.SellerGrossRefundAmount != refund.Amount + subsidyReversal + buyerShippingRecovery)
                throw new InvalidOperationException("Return funding snapshot is inconsistent");
        }
        else if (order.PricingSchemaVersion == 1 && refund.Reason != "shipping-adjustment")
        {
            var payment = await db.Payments.AsNoTracking().SingleAsync(x => x.Id == settlement.PaymentId, ct);
            var buyerPaid = payment.Amount ?? order.TotalPrice ?? 0m;
            SnapshotGross(order, buyerPaid);
            var cumulativeBuyerRefund = await db.Refunds.Where(r => r.OrderId == orderId && r.Status == "Succeeded").SumAsync(r => r.Amount, ct);
            if (cumulativeBuyerRefund > buyerPaid || buyerPaid < 0m)
                throw new InvalidOperationException("Refund would exceed captured buyer payment");
            var alreadyReversed = -await db.FinancialTransactions.Where(x => x.OrderId == orderId && x.Type == "PlatformSubsidyReversal")
                .SumAsync(x => x.Amount, ct);
            var targetReversal = buyerPaid == 0m ? order.PlatformSubsidy : Math.Min(order.PlatformSubsidy, Money(order.PlatformSubsidy * cumulativeBuyerRefund / buyerPaid));
            subsidyReversal = Math.Max(0m, targetReversal - alreadyReversed);
        }

        var held = settlement.Status is "OnHold" or "RefundPending" or "PartiallyRefunded" ? await HeldAmountAsync(settlement.Id, ct) : 0m;
        if (settlement.Status == "RefundPending" && held > 0 && await db.FinancialTransactions.AnyAsync(x => x.EntryKey == $"order:{orderId}:hold:buyer" && x.Amount == 0m, ct))
        {
            // Older buyer-win decisions removed the hold before a refund existed. Restore it in
            // the same save as the refund application so those settlements can be retried safely.
            account.OnHoldBalance += held;
            AddEntry(
                account,
                settlement,
                "HoldCorrection",
                "OnHold",
                held,
                $"order:{orderId}:hold:correction",
                "Restored funds reserved by an earlier buyer-win decision"
            );
        }

        var remainingGross = Math.Max(0m, settlement.GrossAmount - settlement.RefundedAmount);
        var requestedGross = refund.ReturnPolicyVersion == 1 ? refund.SellerGrossRefundAmount : refund.Amount + subsidyReversal;
        if ((order.PricingSchemaVersion == 1 || refund.ReturnPolicyVersion == 1) && requestedGross > remainingGross)
            throw new InvalidOperationException("Refund would exceed remaining seller gross");
        var appliedGross = Math.Min(requestedGross, remainingGross);
        var feeCredit = settlement.GrossAmount == 0m || refund.Reason == "shipping-adjustment" ? 0m : order.PricingSchemaVersion == 1 || refund.ReturnPolicyVersion == 1 ? Math.Max(
            0m,
            Money(settlement.PlatformFeeAmount * (settlement.RefundedAmount + appliedGross) / settlement.GrossAmount) - settlement.FeeCreditAmount
        ) : Money(settlement.PlatformFeeAmount * appliedGross / settlement.GrossAmount);
        feeCredit = Math.Min(feeCredit, settlement.PlatformFeeAmount - settlement.FeeCreditAmount);
        // Buyer-funded return postage was already paid when its label was created.
        var debit = Math.Max(0m, appliedGross - feeCredit - buyerShippingRecovery);
        var refundCredit = Math.Max(0m, feeCredit + buyerShippingRecovery - appliedGross);
        var fromProcessing = Math.Min(settlement.ProcessingAmount, debit);
        settlement.ProcessingAmount -= fromProcessing;
        account.ProcessingBalance -= fromProcessing;
        var left = debit - fromProcessing;
        if (account.OnHoldBalance < held)
            throw new InvalidOperationException("Held seller balance is inconsistent");
        var fromHold = Math.Min(held, left);
        account.OnHoldBalance -= fromHold;
        left -= fromHold;
        var fromAvailable = Math.Min(account.AvailableBalance, left);
        account.AvailableBalance -= fromAvailable;
        left -= fromAvailable;
        if (left > 0)
            account.NegativeBalance += left;
        settlement.RefundedAmount += appliedGross;
        settlement.FeeCreditAmount += feeCredit;
        settlement.ReturnShippingRecovery += buyerShippingRecovery;
        if (refundCredit > 0)
        {
            var recovered = Math.Min(account.NegativeBalance, refundCredit);
            account.NegativeBalance -= recovered;
            account.ProcessingBalance += refundCredit - recovered;
            settlement.ProcessingAmount += refundCredit - recovered;
            if (recovered > 0)
                AddEntry(account, settlement, "DebtRecovery", "Negative", -recovered,
                    $"refund:{refund.Id}:credit-debt", "Refund fee credit recovered seller debt", refund.Id);
        }
        if (refund.Reason != "shipping-adjustment")
            settlement.Status = settlement.RefundedAmount >= settlement.GrossAmount ? "Refunded" : "PartiallyRefunded";
        account.UpdatedAt = Now;
        AddEntry(
            account,
            settlement,
            "Refund",
            "SellerBalance",
            -(appliedGross - subsidyReversal - buyerShippingRecovery),
            $"refund:{refundId}:refund",
            "Refund sent to buyer",
            refundId
        );
        if (fromHold > 0)
            AddEntry(
                account,
                settlement,
                "FundHoldConsumed",
                "OnHold",
                -fromHold,
                $"refund:{refundId}:hold-consumed",
                "Held seller funds consumed by refund",
                refundId
            );
        if (subsidyReversal > 0)
            AddEntry(
                account,
                settlement,
                "PlatformSubsidyReversal",
                "SellerBalance",
                -subsidyReversal,
                $"refund:{refundId}:platform-subsidy-reversal",
                "Platform promotion funding reversed",
                refundId
            );
        if (feeCredit > 0)
            AddEntry(
                account,
                settlement,
                "FeeCredit",
                "SellerBalance",
                feeCredit,
                $"refund:{refundId}:fee-credit",
                "Platform fee credited proportionally",
                refundId
            );
        if (left > 0)
            AddEntry(
                account,
                settlement,
                "NegativeBalance",
                "Negative",
                -left,
                $"refund:{refundId}:negative",
                "Refund exceeded seller funds",
                refundId
            );
        AddEntry(
            account,
            settlement,
            "RefundApplied",
            "Audit",
            0m,
            marker,
            "Refund applied to seller account",
            refundId
        );
        await SyncDebtAsync(account, ct);
        await db.SaveChangesAsync(ct);
        if (refund.ReturnPolicyVersion == 1)
            await ReleaseCompletedReturnRemainderAsync(settlement, account, refund, ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
    }

    public async Task ApplyShippingCostAsync(int orderId, int requestId, decimal amount, CancellationToken ct = default)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await LockOrderAsync(orderId, ct);
        var order = await db.OrderTables.SingleAsync(x => x.Id == orderId, ct);
        await AccountGateAsync(order.SellerId!.Value, ct);
        var key = $"address-change:{requestId}:cost";
        if (await db.FinancialTransactions.AnyAsync(x => x.EntryKey == key, ct))
            return;
        var settlement = await db.SellerSettlements.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (settlement == null)
        {
            var payment = await db.Payments.SingleAsync(x => x.OrderId == orderId && x.Status == "Succeeded", ct);
            settlement = await RecordSuccessfulPaymentAsync(orderId, payment.Id, ct);
        }

        var account = await db.SellerAccounts.SingleAsync(x => x.Id == settlement.SellerAccountId, ct);
        await LockAccountAsync(account, ct);
        var debit = Math.Min(amount, settlement.ProcessingAmount);
        settlement.ProcessingAmount -= debit;
        account.ProcessingBalance -= debit;
        settlement.ShippingAdjustmentCost += amount;
        var fromAvailable = settlement.ReleasedAt != null ? Math.Min(account.AvailableBalance, amount - debit) : 0m;
        account.AvailableBalance -= fromAvailable;
        account.NegativeBalance += amount - debit - fromAvailable;
        account.UpdatedAt = Now;
        AddEntry(
            account,
            settlement,
            "ShippingAdjustmentCost",
            "Processing",
            -debit,
            key,
            "Seller paid additional shipping after address change"
        );
        if (fromAvailable > 0)
            AddEntry(account, settlement, "ShippingAdjustmentCost", "Available", -fromAvailable,
                key + ":available", "Seller wallet paid additional shipping after address change");
        if (amount > debit + fromAvailable)
            AddEntry(
                account,
                settlement,
                "NegativeBalance",
                "Negative",
                -(amount - debit - fromAvailable),
                key + ":negative",
                "Shipping adjustment exceeded seller proceeds"
            );
        await SyncDebtAsync(account, ct);
        await db.SaveChangesAsync(ct);
        if (transaction != null)
            await transaction.CommitAsync(ct);
    }

    public async Task ApplyReturnShippingCostAsync(int orderId, int returnId, decimal amount, CancellationToken ct = default)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await LockOrderAsync(orderId, ct);
        var request = await db.ReturnRequests.SingleAsync(x => x.Id == returnId && x.OrderId == orderId, ct);
        if (request.PricingPolicyVersion != 1 || request.ReturnShippingFee != amount)
            throw new InvalidOperationException("Return shipping cost does not match the accepted quote");
        var settlement = await db.SellerSettlements.SingleAsync(x => x.OrderId == orderId, ct);
        await AccountGateAsync(settlement.SellerId, ct);
        await db.Entry(settlement).ReloadAsync(ct);
        var key = $"return:{returnId}:shipping-cost";
        if (await db.FinancialTransactions.AnyAsync(x => x.EntryKey == key, ct)) return;
        var account = await db.SellerAccounts.SingleAsync(x => x.Id == settlement.SellerAccountId, ct);
        await LockAccountAsync(account, ct);
        var held = settlement.Status is "OnHold" or "RefundPending" ? await HeldAmountAsync(settlement.Id, ct) : 0m;
        if (account.OnHoldBalance < held) throw new InvalidOperationException("Held seller balance is inconsistent");
        var fromProcessing = Math.Min(settlement.ProcessingAmount, amount);
        account.ProcessingBalance -= fromProcessing;
        settlement.ProcessingAmount -= fromProcessing;
        var fromHold = Math.Min(held, amount - fromProcessing);
        account.OnHoldBalance -= fromHold;
        var fromAvailable = Math.Min(account.AvailableBalance, amount - fromProcessing - fromHold);
        account.AvailableBalance -= fromAvailable;
        var debt = amount - fromProcessing - fromHold - fromAvailable;
        account.NegativeBalance += debt;
        settlement.ReturnShippingCost += amount;
        account.UpdatedAt = Now;
        AddEntry(account, settlement, "ReturnShippingCost", "SellerBalance", -amount, key,
            request.ShippingPayer == "Buyer" ? "Return postage advanced; buyer funds it by refund deduction" : "Seller paid return postage");
        if (fromHold > 0)
            AddEntry(account, settlement, "FundHoldConsumed", "OnHold", -fromHold, key + ":hold",
                "Held order funds used for return postage");
        if (debt > 0)
            AddEntry(account, settlement, "NegativeBalance", "Negative", -debt, key + ":negative", "Return postage exceeded seller funds");
        await SyncDebtAsync(account, ct);
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
    }

    private async Task ReleaseCompletedReturnRemainderAsync(SellerSettlement settlement, SellerAccount account, Refund refund, CancellationToken ct)
    {
        if (await db.Disputes.AnyAsync(d => d.OrderId == settlement.OrderId && d.WorkflowEnabled && d.IsOpen
            && !(d.Status == "ExecutingAgreement" && (d.Proposal == "ReturnRefund"
                || db.DisputeDecisions.Any(x => x.DisputeId == d.Id))), ct)) return;
        var held = await HeldAmountAsync(settlement.Id, ct);
        if (account.OnHoldBalance < held) throw new InvalidOperationException("Held seller balance is inconsistent");
        var ready = await CanReleaseAsync(settlement, ct);
        account.OnHoldBalance -= held;
        if (ready)
        {
            var processing = settlement.ProcessingAmount;
            account.AvailableBalance += held + processing;
            account.ProcessingBalance -= processing;
            settlement.ProcessingAmount = 0;
            if (held + processing > 0) settlement.ReleasedAt ??= Now;
            if (settlement.RefundedAmount < settlement.GrossAmount) settlement.Status = "Available";
            if (processing > 0)
                AddEntry(account, settlement, "RefundCreditReleased", "Available", processing,
                    $"refund:{refund.Id}:credit-released", "Return refund fee credit released to wallet", refund.Id);
        }
        else
        {
            account.ProcessingBalance += held;
            settlement.ProcessingAmount += held;
            if (settlement.RefundedAmount < settlement.GrossAmount) settlement.Status = "Processing";
        }
        if (held > 0)
            AddEntry(account, settlement, "HoldReleased", ready ? "Available" : "Processing", held,
                $"refund:{refund.Id}:return-remainder", "Completed return released remaining seller funds", refund.Id);
        account.UpdatedAt = Now;
        RecoverAvailableDebt(account, $"refund:{refund.Id}:remainder-debt");
        await SyncDebtAsync(account, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task PlaceHoldAsync(
        int orderId,
        string reason,
        CancellationToken ct = default,
        int? disputeId = null
    )
    {
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await LockOrderAsync(orderId, ct);
        var settlement = await db.SellerSettlements.SingleAsync(x => x.OrderId == orderId, ct);
        await AccountGateAsync(settlement.SellerId, ct);
        await db.Entry(settlement).ReloadAsync(ct);
        var key = disputeId is null ? $"order:{orderId}:hold" : $"order:{orderId}:case:{disputeId}:hold";
        if (await db.FinancialTransactions.AnyAsync(x => x.EntryKey == key, ct))
            return;
        if (settlement.Status == "OnHold")
            return;
        if (settlement.Status is not ("Processing" or "Available"))
            throw new InvalidOperationException("Order funds cannot be placed on hold");
        var account = await db.SellerAccounts.SingleAsync(x => x.Id == settlement.SellerAccountId, ct);
        await LockAccountAsync(account, ct);
        var holdable = settlement.ProcessingAmount;
        if (holdable > 0)
        {
            settlement.ProcessingAmount = 0;
            account.ProcessingBalance -= holdable;
        }
        else if (settlement.ReleasedAt is not null)
            holdable = Math.Min(account.AvailableBalance, RemainingSellerProceeds(settlement));
        if (holdable > 0)
        {
            if (settlement.ReleasedAt is not null)
                account.AvailableBalance -= holdable;
            account.OnHoldBalance += holdable;
            account.UpdatedAt = Now;
        }

        settlement.Status = "OnHold";
        (await db.OrderTables.SingleAsync(x => x.Id == orderId, ct)).UpdatedAt = Now;
        AddEntry(account, settlement, "FundHold", "OnHold", holdable, key, reason);
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
    }

    public async Task ResolveHoldAsync(
        int orderId,
        bool releaseToSeller,
        CancellationToken ct = default,
        int? disputeId = null
    )
    {
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await LockOrderAsync(orderId, ct);
        var settlement = await db.SellerSettlements.SingleAsync(x => x.OrderId == orderId, ct);
        await AccountGateAsync(settlement.SellerId, ct);
        await db.Entry(settlement).ReloadAsync(ct);
        await InitializeDeliveryScheduleAsync(settlement, ct);
        var prefix = disputeId is null ? $"order:{orderId}:hold" : $"order:{orderId}:case:{disputeId}:hold";
        var key = $"{prefix}:{(releaseToSeller ? "seller" : "buyer")}";
        if (await db.FinancialTransactions.AnyAsync(x => x.EntryKey == key, ct))
            return;
        if (settlement.Status != "OnHold")
            throw new InvalidOperationException("Order funds are not on hold");
        var account = await db.SellerAccounts.SingleAsync(x => x.Id == settlement.SellerAccountId, ct);
        await LockAccountAsync(account, ct);
        var held = await HeldAmountAsync(settlement.Id, ct);
        if (account.OnHoldBalance < held)
            throw new InvalidOperationException("Held seller balance is inconsistent");
        if (releaseToSeller)
        {
            account.OnHoldBalance -= held;
            if (await CanReleaseAsync(settlement, ct))
            {
                account.AvailableBalance += held;
                settlement.Status = "Available";
                settlement.ReleasedAt ??= Now;
                AddEntry(account, settlement, "HoldReleased", "Available", held, key, "Dispute resolved for seller");
            }
            else
            {
                account.ProcessingBalance += held;
                settlement.ProcessingAmount += held;
                settlement.Status = "Processing";
                AddEntry(
                    account,
                    settlement,
                    "HoldReleased",
                    "Processing",
                    held,
                    key,
                    "Dispute resolved for seller before funds became available"
                );
            }
        }
        else
        {
            settlement.Status = "RefundPending";
            AddEntry(
                account,
                settlement,
                "HoldReserved",
                "OnHold",
                held,
                key,
                "Dispute resolved for buyer; held funds remain reserved until refund"
            );
        }

        account.UpdatedAt = Now;
        (await db.OrderTables.SingleAsync(x => x.Id == orderId, ct)).UpdatedAt = account.UpdatedAt;
        if (releaseToSeller) RecoverAvailableDebt(account, key + ":debt");
        await SyncDebtAsync(account, ct);
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
    }

    public async Task<int> BackfillAsync(CancellationToken ct = default)
    {
        var payments = await db.Payments.AsNoTracking().Where(x => x.Status == "Succeeded" && x.OrderId != null && db.OrderTables.Any(o => o.Id == x.OrderId && o.SellerId != null) && !db.SellerSettlements.Any(s => s.OrderId == x.OrderId))
            .OrderBy(x => x.Id)
            .Take(50)
            .ToListAsync(ct);
        var count = 0;
        foreach (var payment in payments)
        {
            await RecordSuccessfulPaymentAsync(payment.OrderId!.Value, payment.Id, ct);
            count++;
        }

        var refunds = await db.Refunds.AsNoTracking().Where(x => x.Status == "Succeeded" && db.SellerSettlements.Any(s => s.OrderId == x.OrderId))
            .OrderBy(x => x.Id)
            .Take(100)
            .ToListAsync(ct);
        foreach (var refund in refunds)
        {
            if (!await db.FinancialTransactions.AnyAsync(x => x.EntryKey == $"refund:{refund.Id}:applied", ct))
            {
                await ApplyRefundAsync(refund.OrderId, refund.Id, ct);
                count++;
            }
        }

        return count;
    }

    public async Task<int> ReleaseDueFundsAsync(CancellationToken ct = default)
    {
        var now = Now;
        var due = await db.SellerSettlements.Where(x => x.Status == "Processing" &&
            (x.ReleaseAt <= now || x.ReleaseAt == null && db.ShippingInfos.Any(s => s.OrderId == x.OrderId && s.Direction == "Outbound" && s.DeliveredAt != null)) &&
            !db.ReturnRequests.Any(r => r.OrderId == x.OrderId && r.Status != "Rejected" && r.Status != "Refunded" && r.Status != "Closed") &&
            !db.Disputes.Any(d => d.OrderId == x.OrderId && d.WorkflowEnabled && d.IsOpen))
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.OrderId })
            .Take(100)
            .ToListAsync(ct);
        var count = 0;
        foreach (var candidate in due)
        {
            await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
            if (db.Database.IsSqlServer())
                await db.OrderTables.FromSqlInterpolated($"SELECT * FROM [OrderTable] WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {candidate.OrderId}")
                    .SingleAsync(ct);
            var settlement = await db.SellerSettlements.SingleAsync(x => x.Id == candidate.Id, ct);
            await AccountGateAsync(settlement.SellerId, ct);
            await db.Entry(settlement).ReloadAsync(ct);
            await InitializeDeliveryScheduleAsync(settlement, ct);
            // Persist a recovered schedule even while its hold period is still running.
            await db.SaveChangesAsync(ct);
            if (settlement.Status != "Processing" || !await CanReleaseAsync(settlement, ct) || await db.ReturnRequests.AnyAsync(
                r => r.OrderId == candidate.OrderId && r.Status != "Rejected" && r.Status != "Refunded" && r.Status != "Closed",
                ct
            ) || await db.Disputes.AnyAsync(d => d.OrderId == candidate.OrderId && d.WorkflowEnabled && d.IsOpen, ct))
            {
                if (transaction != null) await transaction.CommitAsync(ct);
                continue;
            }
            var account = await db.SellerAccounts.SingleAsync(x => x.Id == settlement.SellerAccountId, ct);
            await LockAccountAsync(account, ct);
            ReleaseToWallet(settlement, account, "Hold period completed after delivery");
            await SyncDebtAsync(account, ct);
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
            count++;
        }

        return count;
    }

    public async Task<SellerPayout> RequestPayoutAsync(
        int sellerId,
        decimal amount,
        string key,
        bool simulateFailure,
        CancellationToken ct = default
    )
    {
        if (amount <= 0 || Money(amount) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            throw new ArgumentException("Payout key is required", nameof(key));
        // The seller gate serializes balances; ReadCommitted avoids shared key-range
        // locks on absent request keys blocking unrelated sellers' inserts.
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct) : null;
        await AccountGateAsync(sellerId, ct);
        var account = await GetOrCreateAccountAsync(sellerId, ct);
        var existing = await db.SellerPayouts.SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            if (existing.SellerAccountId != account.Id || existing.Amount != decimal.Round(amount, 2) || existing.SimulateFailure != simulateFailure)
                throw new InvalidOperationException("Payout key belongs to a different request");
            if (tx != null) await tx.CommitAsync(ct);
            return existing;
        }

        await LockAccountAsync(account, ct);
        if (account.Status != "Active") throw new InvalidOperationException("Seller account is restricted");
        if (account.PermanentSalesBlockedAt != null) throw new InvalidOperationException("Tài khoản bị khóa bán vĩnh viễn, không thể tạo payout mới.");
        if (account.NegativeBalance > 0)
            throw new InvalidOperationException("Negative balance must be recovered before payout");
        if (account.AvailableBalance < amount)
            throw new InvalidOperationException("Available balance is insufficient");
        var bankIdentity = await db.SellerIdentities.AsNoTracking().Where(x => x.SellerId == sellerId && x.Kind == "BANK")
            .OrderByDescending(x => x.Id).Select(x => x.MaskedValue).FirstOrDefaultAsync(ct);
        var now = Now;
        account.AvailableBalance -= amount;
        account.UpdatedAt = now;
        var payout = new SellerPayout
        {
            SellerAccountId = account.Id,
            Amount = decimal.Round(amount, 2),
            IdempotencyKey = key,
            DestinationMasked = bankIdentity == null ? "****1234" : bankIdentity.Split(" · ")[0] + " · ****" + bankIdentity[^4..],
            SimulateFailure = simulateFailure,
            Status = account.Level >= 3 && !simulateFailure ? "Completed" : "Created",
            CreatedAt = now,
            UpdatedAt = now,
            CompletedAt = account.Level >= 3 && !simulateFailure ? now : null,
            BankReferenceId = account.Level >= 3 && !simulateFailure ? "BANK-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant() : null
        };
        db.SellerPayouts.Add(payout);
        await db.SaveChangesAsync(ct);
        AddEntry(
            account,
            null,
            "Payout",
            "Available",
            -payout.Amount,
            $"payout:{payout.Id}:created",
            "Payout requested",
            payoutId: payout.Id
        );
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return payout;
    }

    public async Task<int> AdvancePayoutsAsync(CancellationToken ct = default)
    {
        var payouts = await db.SellerPayouts.AsNoTracking().Where(x => x.Status != "Completed" && x.Status != "Returned").OrderBy(x => x.Id)
            .Take(50)
            .Select(x => new { x.Id, SellerId = db.SellerAccounts.Where(a => a.Id == x.SellerAccountId).Select(a => a.SellerId).Single() })
            .ToListAsync(ct);
        foreach (var candidate in payouts)
        {
            await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
            await AccountGateAsync(candidate.SellerId, ct);
            var payout = await db.SellerPayouts.SingleAsync(x => x.Id == candidate.Id, ct);
            await db.Entry(payout).ReloadAsync(ct);
            if (payout.Status is "Completed" or "Returned")
            {
                if (tx != null) await tx.CommitAsync(ct);
                continue;
            }
            var account = await db.SellerAccounts.SingleAsync(x => x.Id == payout.SellerAccountId, ct);
            await LockAccountAsync(account, ct);
            payout.UpdatedAt = Now;
            if (payout.Status == "Created")
                payout.Status = "InProgress";
            else if (payout.Status == "InProgress")
            {
                payout.Status = "FundsSent";
                payout.BankReferenceId = "BANK-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            }
            else if (payout.SimulateFailure)
            {
                payout.Status = "Returned";
                payout.CompletedAt = Now;
                account.AvailableBalance += payout.Amount;
                account.UpdatedAt = Now;
                AddEntry(
                    account,
                    null,
                    "PayoutReturned",
                    "Available",
                    payout.Amount,
                    $"payout:{payout.Id}:returned",
                    "Bank rejected payout; funds restored",
                    payoutId: payout.Id
                );
                RecoverAvailableDebt(account, $"payout:{payout.Id}:returned-debt");
            }
            else
            {
                payout.Status = "Completed";
                payout.CompletedAt = Now;
            }
            await SyncDebtAsync(account, ct);
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
        }

        await db.SaveChangesAsync(ct);
        return payouts.Count;
    }

    public async Task<SellerFinanceSummary> GetSummaryAsync(int sellerId, CancellationToken ct = default)
    {
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await AccountGateAsync(sellerId, ct);
        var account = await GetOrCreateAccountAsync(sellerId, ct);
        await LockAccountAsync(account, ct);
        ResetMonth(account);
        await db.SaveChangesAsync(ct);
        if (tx != null) { await tx.CommitAsync(ct); await tx.DisposeAsync(); }
        return await BuildSummaryAsync(account, ct);
    }

    public async Task<SellerFinanceSummary> EvaluateLevelAsync(int sellerId, CancellationToken ct = default)
    {
        var observed = await db.SellerAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.SellerId == sellerId, ct) ?? new SellerAccount { SellerId = sellerId };
        var policy = SellerLevelRules.Policy(configuration);
        var metrics = await ReadLevelMetricsAsync(observed, policy, ct);
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await AccountGateAsync(sellerId, ct);
        var account = await GetOrCreateAccountAsync(sellerId, ct);
        await LockAccountAsync(account, ct);
        await EvaluateAndSaveLevelAsync(account, metrics, policy, "SellerRequest", ct);
        if (tx != null) { await tx.CommitAsync(ct); await tx.DisposeAsync(); }
        return await BuildSummaryAsync(account, ct);
    }

    private async Task<SellerFinanceSummary> BuildSummaryAsync(SellerAccount account, CancellationToken ct)
    {
        var metrics = await ReadLevelMetricsAsync(account, SellerLevelRules.Policy(configuration), ct);
        var target = account.Level switch
        {
            1 => (2, Level2Orders, Level2Feedback, Level2OnTime),
            2 => (3, Level3Orders, Level3Feedback, Level3OnTime),
            _ => (3, Level3Orders, Level3Feedback, Level3OnTime)};
        var transactions = await db.FinancialTransactions.AsNoTracking().Where(x => x.SellerAccountId == account.Id && x.Type != "RefundApplied")
            .OrderByDescending(x => x.Id)
            .Take(50)
            .Select(x => new FinanceTransactionView(x.Id, x.OrderId, x.Type, x.Bucket, x.Amount, x.Currency, x.Description, x.CreatedAt))
            .ToListAsync(ct);
        var payouts = await db.SellerPayouts.AsNoTracking().Where(x => x.SellerAccountId == account.Id).OrderByDescending(x => x.Id)
            .Take(20)
            .Select(x => new PayoutView(
            x.Id,
            x.Amount,
            x.Currency,
            x.Status,
            x.DestinationMasked,
            x.BankReferenceId,
            x.CreatedAt,
            x.CompletedAt
        ))
            .ToListAsync(ct);
        return new SellerFinanceSummary(
            account.SellerId,
            account.Level,
            account.Level >= 3 ? "Instant" : $"Level {account.Level}",
            account.Status,
            account.MonthlySalesLimit,
            account.MonthlySalesAmount,
            account.HoldDays,
            account.ProcessingBalance,
            account.AvailableBalance,
            account.OnHoldBalance,
            account.NegativeBalance,
            new SellerLevelProgress(
            metrics.CompletedOrders,
            metrics.PositiveFeedbackRate,
            metrics.OnTimeDeliveryRate,
            target.Item1,
            target.Item2,
            target.Item3,
            target.Item4
        ),
            transactions,
            payouts,
            HoldSeconds(account.Level, account.HoldDays)
        );
    }

    private async Task<SellerAccount> GetOrCreateAccountAsync(int sellerId, CancellationToken ct)
    {
        var account = await db.SellerAccounts.SingleOrDefaultAsync(x => x.SellerId == sellerId, ct);
        if (account is not null)
            return account;
        account = new SellerAccount
        {
            SellerId = sellerId,
            SalesMonth = MonthStart(),
            UpdatedAt = Now
        };
        ApplyLevel(account, 1);
        db.SellerAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account;
    }

    private void ApplyLevel(SellerAccount account, int level)
    {
        account.Level = level;
        account.MonthlySalesLimit = level switch
        {
            1 => Level1Limit,
            2 => Level2Limit,
            _ => Level3Limit
        };
        account.HoldDays = level switch
        {
            1 => Level1HoldDays,
            2 => Level2HoldDays,
            _ => 0
        };
    }

    private void ResetMonth(SellerAccount account)
    {
        var month = MonthStart();
        if (account.SalesMonth >= month)
            return;
        account.SalesMonth = month;
        account.MonthlySalesAmount = 0m;
        account.UpdatedAt = Now;
    }

    private int HoldSeconds(int level, int holdDays)
    {
        if (level == 3) return 0;
        var seconds = configuration.GetValue<int?>($"Finance:Levels:{level}:HoldSeconds");
        if (seconds is < 0) throw new InvalidOperationException("Hold seconds cannot be negative");
        return seconds ?? checked(holdDays * 86400);
    }

    public async Task StartDeliveryHoldAsync(int orderId, CancellationToken ct = default)
    {
        // Called inside the tracking transaction; a worker can recover this from DeliveredAt too.
        var settlement = await db.SellerSettlements.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (settlement == null) return;
        await InitializeDeliveryScheduleAsync(settlement, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task InitializeDeliveryScheduleAsync(SellerSettlement settlement, CancellationToken ct)
    {
        if (settlement.HoldPolicyVersion != 1 || settlement.SellerLevelSnapshot == 3 || settlement.HoldStartedAt != null)
            return;
        var deliveredAt = await db.ShippingInfos.Where(s => s.OrderId == settlement.OrderId && s.Direction == "Outbound" && s.DeliveredAt != null)
            .Select(s => s.DeliveredAt).SingleOrDefaultAsync(ct);
        if (deliveredAt == null) return;
        settlement.HoldStartedAt = deliveredAt;
        settlement.ReleaseAt = deliveredAt.Value.AddSeconds(settlement.HoldSecondsSnapshot);
    }

    private async Task<bool> CanReleaseAsync(SellerSettlement settlement, CancellationToken ct)
    {
        if (settlement.ReleaseAt == null || settlement.ReleaseAt > Now) return false;
        if (settlement.HoldPolicyVersion == 1 && settlement.SellerLevelSnapshot == 3) return true;
        return await db.OrderTables.AnyAsync(o => o.Id == settlement.OrderId && (o.Status == "Delivered" || o.Status == "Closed"), ct)
            && (settlement.HoldPolicyVersion == 0 || settlement.HoldStartedAt != null);
    }

    private void ReleaseToWallet(SellerSettlement settlement, SellerAccount account, string description)
    {
        var amount = settlement.ProcessingAmount;
        account.ProcessingBalance -= amount;
        account.AvailableBalance += amount;
        account.UpdatedAt = Now;
        settlement.ProcessingAmount = 0;
        settlement.Status = "Available";
        settlement.ReleasedAt = Now;
        AddEntry(account, settlement, "FundsReleased", "Available", amount,
            $"order:{settlement.OrderId}:released", description);
        RecoverAvailableDebt(account, $"order:{settlement.OrderId}:released-debt");
    }

    private void AddEntry(
        SellerAccount account,
        SellerSettlement? settlement,
        string type,
        string bucket,
        decimal amount,
        string key,
        string description,
        int? refundId = null,
        int? payoutId = null
    )
    {
        db.FinancialTransactions.Add(new FinancialTransaction { SellerAccountId = account.Id, OrderId = settlement?.OrderId, SettlementId = settlement?.Id, RefundId = refundId, PayoutId = payoutId, Type = type, Bucket = bucket, Amount = decimal.Round(amount, 2), EntryKey = key, Description = description, CreatedAt = Now });
    }

    private DateTime MonthStart() => new(Now.Year, Now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    private static decimal Money(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    private static decimal SnapshotGross(OrderTable order, decimal buyerPaid)
    {
        if (order.PricingSchemaVersion != 1)
            return buyerPaid;
        if (buyerPaid < 0 || order.PlatformSubsidy < 0 || order.SellerGrossSnapshot < 0 || order.SellerGrossSnapshot != Money(buyerPaid + order.PlatformSubsidy))
            throw new InvalidOperationException("Promotion funding snapshot does not match buyer payment");
        return order.SellerGrossSnapshot;
    }

    private async Task LockOrderAsync(int orderId, CancellationToken ct)
    {
        if (db.Database.IsSqlServer())
            await db.OrderTables.FromSqlInterpolated($"SELECT * FROM [OrderTable] WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {orderId}")
                .AsNoTracking()
                .SingleAsync(ct);
    }

    private async Task LockAccountAsync(SellerAccount account, CancellationToken ct)
    {
        if (db.Database.IsSqlServer())
            await db.SellerAccounts.FromSqlInterpolated($"SELECT * FROM [SellerAccount] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {account.Id}")
                .SingleAsync(ct);
        await db.Entry(account).ReloadAsync(ct);
        await SyncDebtAsync(account, ct);
    }

    private async Task AccountGateAsync(int sellerId, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer())
            return;
        // Acquire before the first account read: Serializable shared locks from two
        // orders must not both reach the later balance update and require conversion.
        var resource = $"commerce:seller-finance:{sellerId}";
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = {resource},
                @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 10000;
            IF @result < 0 THROW 51000, 'Cannot acquire seller finance transaction lock', 1;", ct);
    }

    private static decimal RemainingSellerProceeds(SellerSettlement settlement) => Math.Max(
        0m,
        settlement.NetAmount - (settlement.RefundedAmount - settlement.FeeCreditAmount) - settlement.ShippingAdjustmentCost
            - settlement.ReturnShippingCost + settlement.ReturnShippingRecovery
    );
    private async Task<decimal> HeldAmountAsync(int settlementId, CancellationToken ct)
    {
        var hold = await db.FinancialTransactions.Where(x => x.SettlementId == settlementId && x.Type == "FundHold")
            .OrderByDescending(x => x.Id)
            .Select(x => new { x.Id, x.Amount })
            .FirstOrDefaultAsync(ct);
        if (hold is null)
            return 0m;
        var consumed = -await db.FinancialTransactions.Where(x => x.SettlementId == settlementId && x.Id > hold.Id && x.Type == "FundHoldConsumed")
            .SumAsync(x => x.Amount, ct);
        var released = await db.FinancialTransactions.Where(x => x.SettlementId == settlementId && x.Id > hold.Id && x.Type == "HoldReleased")
            .SumAsync(x => x.Amount, ct);
        return Math.Max(0m, hold.Amount - consumed - released);
    }

    private decimal Level1Limit => configuration.GetValue("Finance:Levels:1:MonthlySalesLimit", 5_000m);
    private decimal Level2Limit => configuration.GetValue("Finance:Levels:2:MonthlySalesLimit", 10_000m);
    private decimal Level3Limit => configuration.GetValue("Finance:Levels:3:MonthlySalesLimit", 1_000_000m);
    private int Level1HoldDays => configuration.GetValue("Finance:Levels:1:HoldDays", 21);
    private int Level2HoldDays => configuration.GetValue("Finance:Levels:2:HoldDays", 7);
    private int Level2Orders => configuration.GetValue("Finance:LevelPromotion:Level2:CompletedOrders", 20);
    private decimal Level2Feedback => configuration.GetValue("Finance:LevelPromotion:Level2:PositiveFeedbackRate", 90m);
    private decimal Level2OnTime => configuration.GetValue("Finance:LevelPromotion:Level2:OnTimeDeliveryRate", 90m);
    private int Level3Orders => configuration.GetValue("Finance:LevelPromotion:Level3:CompletedOrders", 100);
    private decimal Level3Feedback => configuration.GetValue("Finance:LevelPromotion:Level3:PositiveFeedbackRate", 97m);
    private decimal Level3OnTime => configuration.GetValue("Finance:LevelPromotion:Level3:OnTimeDeliveryRate", 95m);
}
