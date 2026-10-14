using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Finance;
public sealed class SellerFinanceService(ApplicationDbContext db, IConfiguration configuration, TimeProvider? clock = null) : ISellerFinanceService
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
        if (account.MonthlySalesAmount + SnapshotGross(order, order.TotalPrice ?? 0m) > account.MonthlySalesLimit)
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
        if (account.MonthlySalesAmount + gross > account.MonthlySalesLimit)
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
        if (order.PricingSchemaVersion == 1 && refund.Reason != "shipping-adjustment")
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
        var requestedGross = refund.Amount + subsidyReversal;
        if (order.PricingSchemaVersion == 1 && requestedGross > remainingGross)
            throw new InvalidOperationException("Refund would exceed remaining seller gross");
        var appliedGross = Math.Min(requestedGross, remainingGross);
        var feeCredit = settlement.GrossAmount == 0m || refund.Reason == "shipping-adjustment" ? 0m : order.PricingSchemaVersion == 1 ? Math.Max(
            0m,
            Money(settlement.PlatformFeeAmount * (settlement.RefundedAmount + appliedGross) / settlement.GrossAmount) - settlement.FeeCreditAmount
        ) : Money(settlement.PlatformFeeAmount * appliedGross / settlement.GrossAmount);
        feeCredit = Math.Min(feeCredit, settlement.PlatformFeeAmount - settlement.FeeCreditAmount);
        var debit = Math.Max(0m, appliedGross - feeCredit);
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
        if (refund.Reason != "shipping-adjustment")
            settlement.Status = settlement.RefundedAmount >= settlement.GrossAmount ? "Refunded" : "PartiallyRefunded";
        account.UpdatedAt = Now;
        AddEntry(
            account,
            settlement,
            "Refund",
            "SellerBalance",
            -(appliedGross - subsidyReversal),
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
        await db.SaveChangesAsync(ct);
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
        await db.SaveChangesAsync(ct);
        if (transaction != null)
            await transaction.CommitAsync(ct);
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
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            throw new ArgumentException("Payout key is required", nameof(key));
        var account = await GetOrCreateAccountAsync(sellerId, ct);
        var existing = await db.SellerPayouts.SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            if (existing.SellerAccountId != account.Id || existing.Amount != decimal.Round(amount, 2) || existing.SimulateFailure != simulateFailure)
                throw new InvalidOperationException("Payout key belongs to a different request");
            return existing;
        }

        if (account.NegativeBalance > 0)
            throw new InvalidOperationException("Negative balance must be recovered before payout");
        if (account.AvailableBalance < amount)
            throw new InvalidOperationException("Available balance is insufficient");
        var now = Now;
        account.AvailableBalance -= amount;
        account.UpdatedAt = now;
        var payout = new SellerPayout
        {
            SellerAccountId = account.Id,
            Amount = decimal.Round(amount, 2),
            IdempotencyKey = key,
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
        return payout;
    }

    public async Task<int> AdvancePayoutsAsync(CancellationToken ct = default)
    {
        var payouts = await db.SellerPayouts.Where(x => x.Status != "Completed" && x.Status != "Returned").OrderBy(x => x.Id)
            .Take(50)
            .ToListAsync(ct);
        foreach (var payout in payouts)
        {
            var account = await db.SellerAccounts.SingleAsync(x => x.Id == payout.SellerAccountId, ct);
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
            }
            else
            {
                payout.Status = "Completed";
                payout.CompletedAt = Now;
            }
        }

        await db.SaveChangesAsync(ct);
        return payouts.Count;
    }

    public async Task<SellerFinanceSummary> GetSummaryAsync(int sellerId, CancellationToken ct = default)
    {
        var account = await GetOrCreateAccountAsync(sellerId, ct);
        ResetMonth(account);
        await db.SaveChangesAsync(ct);
        return await BuildSummaryAsync(account, ct);
    }

    public async Task<SellerFinanceSummary> EvaluateLevelAsync(int sellerId, CancellationToken ct = default)
    {
        var account = await GetOrCreateAccountAsync(sellerId, ct);
        var metrics = await GetMetricsAsync(sellerId, ct);
        var newLevel = metrics.CompletedOrders >= Level3Orders && metrics.PositiveFeedbackRate >= Level3Feedback && metrics.OnTimeDeliveryRate >= Level3OnTime ? 3 : metrics.CompletedOrders >= Level2Orders && metrics.PositiveFeedbackRate >= Level2Feedback && metrics.OnTimeDeliveryRate >= Level2OnTime ? 2 : 1;
        ApplyLevel(account, newLevel);
        account.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return await BuildSummaryAsync(account, ct);
    }

    private async Task<SellerFinanceSummary> BuildSummaryAsync(SellerAccount account, CancellationToken ct)
    {
        var metrics = await GetMetricsAsync(account.SellerId, ct);
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
    }

    private async Task<(int CompletedOrders, decimal PositiveFeedbackRate, decimal OnTimeDeliveryRate)> GetMetricsAsync(int sellerId, CancellationToken ct)
    {
        var completedOrders = await db.OrderTables.CountAsync(x => x.SellerId == sellerId && (x.Status == "Delivered" || x.Status == "Closed"), ct);
        var feedback = await db.Feedbacks.AsNoTracking().Where(x => x.SellerId == sellerId).Select(x => x.PositiveRate)
            .FirstOrDefaultAsync(ct) ?? 0m;
        var delivered = await db.ShippingInfos.AsNoTracking().Where(x => x.Direction == "Outbound" && x.Status == "Delivered" && db.OrderTables.Any(o => o.Id == x.OrderId && o.SellerId == sellerId))
            .Select(x => x.DeliveryAttempts)
            .ToListAsync(ct);
        var onTime = delivered.Count == 0 ? 0m : decimal.Round(delivered.Count(x => x <= 1) * 100m / delivered.Count, 2);
        return (completedOrders, feedback, onTime);
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
