using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Finance;

public sealed class SellerFinanceService(ApplicationDbContext db, IConfiguration configuration) : ISellerFinanceService
{
    private decimal FeePercent => configuration.GetValue("Finance:PlatformFeePercent", 12m);
    private decimal FixedFee => configuration.GetValue("Finance:FixedOrderFee", 0.30m);

    public async Task ValidateMonthlyLimitAsync(int orderId, CancellationToken ct = default)
    {
        var order = await db.OrderTables.AsNoTracking().SingleAsync(x => x.Id == orderId, ct);
        var sellerId = order.SellerId ?? throw new InvalidOperationException("Order has no seller");
        var account = await GetOrCreateAccountAsync(sellerId, ct);
        ResetMonth(account);
        if (account.Status != "Active") throw new InvalidOperationException("Seller account is restricted");
        if (account.MonthlySalesAmount + (order.TotalPrice ?? 0m) > account.MonthlySalesLimit)
            throw new InvalidOperationException($"Seller level {account.Level} monthly sales limit would be exceeded");
        await db.SaveChangesAsync(ct);
    }

    public async Task<SellerSettlement> RecordSuccessfulPaymentAsync(int orderId, int paymentId, CancellationToken ct = default)
    {
        var existing = await db.SellerSettlements.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (existing is not null) return existing;

        var order = await db.OrderTables.AsNoTracking().SingleAsync(x => x.Id == orderId, ct);
        var payment = await db.Payments.AsNoTracking().SingleAsync(x => x.Id == paymentId, ct);
        if (payment.Status != "Succeeded") throw new InvalidOperationException("Only successful payments can be settled");
        var sellerId = order.SellerId ?? throw new InvalidOperationException("Order has no seller");
        var gross = payment.Amount ?? order.TotalPrice ?? 0m;
        var fee = SellerFinanceRules.CalculateFees(gross, FeePercent, FixedFee);
        var account = await GetOrCreateAccountAsync(sellerId, ct);
        ResetMonth(account);
        if (account.MonthlySalesAmount + gross > account.MonthlySalesLimit)
            throw new InvalidOperationException($"Seller level {account.Level} monthly sales limit exceeded");

        var now = DateTime.UtcNow;
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
            ReleaseAt = ResolveReleaseAt(now, account.HoldDays)
        };
        db.SellerSettlements.Add(settlement);
        await db.SaveChangesAsync(ct);

        var recovered = Math.Min(account.NegativeBalance, settlement.ProcessingAmount);
        account.NegativeBalance -= recovered;
        settlement.ProcessingAmount -= recovered;
        account.ProcessingBalance += settlement.ProcessingAmount;
        account.MonthlySalesAmount += gross;
        account.UpdatedAt = now;

        AddEntry(account, settlement, "Sale", "Processing", gross, $"order:{orderId}:sale", "Buyer payment received");
        AddEntry(account, settlement, "PlatformFee", "Processing", -fee.TotalFee, $"order:{orderId}:fee", $"Platform fee {FeePercent:0.##}% + {fee.FixedFee:0.00}");
        if (recovered > 0)
            AddEntry(account, settlement, "DebtRecovery", "Negative", -recovered, $"order:{orderId}:debt-recovery", "Previous negative balance recovered from sale proceeds");
        await db.SaveChangesAsync(ct);
        return settlement;
    }

    public async Task ApplyRefundAsync(int orderId, int refundId, CancellationToken ct = default)
    {
        var marker = $"refund:{refundId}:applied";
        if (await db.FinancialTransactions.AnyAsync(x => x.EntryKey == marker, ct)) return;
        var refund = await db.Refunds.AsNoTracking().SingleAsync(x => x.Id == refundId, ct);
        if (refund.Status != "Succeeded") throw new InvalidOperationException("Only successful refunds affect seller balance");
        var settlement = await db.SellerSettlements.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (settlement is null)
        {
            var paymentId = await db.Payments.Where(x => x.OrderId == orderId && x.Status == "Succeeded")
                .Select(x => x.Id).FirstAsync(ct);
            settlement = await RecordSuccessfulPaymentAsync(orderId, paymentId, ct);
        }
        var account = await db.SellerAccounts.SingleAsync(x => x.Id == settlement.SellerAccountId, ct);
        var held = settlement.Status is "OnHold" or "RefundPending"
            ? await HeldAmountAsync(settlement.Id, ct) : 0m;
        if (settlement.Status == "RefundPending" && held > 0 &&
            await db.FinancialTransactions.AnyAsync(x => x.EntryKey == $"order:{orderId}:hold:buyer" && x.Amount == 0m, ct))
        {
            // Older buyer-win decisions removed the hold before a refund existed. Restore it in
            // the same save as the refund application so those settlements can be retried safely.
            account.OnHoldBalance += held;
            AddEntry(account, settlement, "HoldCorrection", "OnHold", held,
                $"order:{orderId}:hold:correction", "Restored funds reserved by an earlier buyer-win decision");
        }
        var remainingGross = Math.Max(0m, settlement.GrossAmount - settlement.RefundedAmount);
        var appliedGross = Math.Min(refund.Amount, remainingGross);
        var feeCredit = settlement.GrossAmount == 0m ? 0m :
            decimal.Round(settlement.PlatformFeeAmount * appliedGross / settlement.GrossAmount, 2, MidpointRounding.AwayFromZero);
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
        if (left > 0) account.NegativeBalance += left;

        settlement.RefundedAmount += appliedGross;
        settlement.FeeCreditAmount += feeCredit;
        settlement.Status = settlement.RefundedAmount >= settlement.GrossAmount ? "Refunded" : "PartiallyRefunded";
        account.UpdatedAt = DateTime.UtcNow;
        AddEntry(account, settlement, "Refund", "SellerBalance", -appliedGross, $"refund:{refundId}:refund", "Refund sent to buyer", refundId);
        if (feeCredit > 0) AddEntry(account, settlement, "FeeCredit", "SellerBalance", feeCredit, $"refund:{refundId}:fee-credit", "Platform fee credited proportionally", refundId);
        if (left > 0) AddEntry(account, settlement, "NegativeBalance", "Negative", -left, $"refund:{refundId}:negative", "Refund exceeded seller funds", refundId);
        AddEntry(account, settlement, "RefundApplied", "Audit", 0m, marker, "Refund applied to seller account", refundId);
        await db.SaveChangesAsync(ct);
    }

    public async Task PlaceHoldAsync(int orderId, string reason, CancellationToken ct = default)
    {
        var settlement = await db.SellerSettlements.SingleAsync(x => x.OrderId == orderId, ct);
        var key = $"order:{orderId}:hold";
        if (await db.FinancialTransactions.AnyAsync(x => x.EntryKey == key, ct)) return;
        if (settlement.Status is not ("Processing" or "Available"))
            throw new InvalidOperationException("Order funds cannot be placed on hold");
        var account = await db.SellerAccounts.SingleAsync(x => x.Id == settlement.SellerAccountId, ct);
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
            if (settlement.ReleasedAt is not null) account.AvailableBalance -= holdable;
            account.OnHoldBalance += holdable;
            account.UpdatedAt = DateTime.UtcNow;
        }
        settlement.Status = "OnHold";
        (await db.OrderTables.SingleAsync(x => x.Id == orderId, ct)).UpdatedAt = DateTime.UtcNow;
        AddEntry(account, settlement, "FundHold", "OnHold", holdable, key, reason);
        await db.SaveChangesAsync(ct);
    }

    public async Task ResolveHoldAsync(int orderId, bool releaseToSeller, CancellationToken ct = default)
    {
        var settlement = await db.SellerSettlements.SingleAsync(x => x.OrderId == orderId, ct);
        var key = $"order:{orderId}:hold:{(releaseToSeller ? "seller" : "buyer")}";
        if (await db.FinancialTransactions.AnyAsync(x => x.EntryKey == key, ct)) return;
        if (settlement.Status != "OnHold") throw new InvalidOperationException("Order funds are not on hold");
        var account = await db.SellerAccounts.SingleAsync(x => x.Id == settlement.SellerAccountId, ct);
        var held = await HeldAmountAsync(settlement.Id, ct);
        if (account.OnHoldBalance < held)
            throw new InvalidOperationException("Held seller balance is inconsistent");
        if (releaseToSeller)
        {
            account.OnHoldBalance -= held;
            var orderStatus = await db.OrderTables.Where(x => x.Id == orderId).Select(x => x.Status).SingleAsync(ct);
            if (settlement.ReleaseAt <= DateTime.UtcNow && orderStatus is ("Delivered" or "Closed"))
            {
                account.AvailableBalance += held;
                settlement.Status = "Available";
                settlement.ReleasedAt ??= DateTime.UtcNow;
                AddEntry(account, settlement, "HoldReleased", "Available", held, key, "Dispute resolved for seller");
            }
            else
            {
                account.ProcessingBalance += held;
                settlement.ProcessingAmount += held;
                settlement.Status = "Processing";
                AddEntry(account, settlement, "HoldReleased", "Processing", held, key, "Dispute resolved for seller before funds became available");
            }
        }
        else
        {
            settlement.Status = "RefundPending";
            AddEntry(account, settlement, "HoldReserved", "OnHold", held, key, "Dispute resolved for buyer; held funds remain reserved until refund");
        }
        account.UpdatedAt = DateTime.UtcNow;
        (await db.OrderTables.SingleAsync(x => x.Id == orderId, ct)).UpdatedAt = account.UpdatedAt;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> BackfillAsync(CancellationToken ct = default)
    {
        var payments = await db.Payments.AsNoTracking()
            .Where(x => x.Status == "Succeeded" && x.OrderId != null &&
                db.OrderTables.Any(o => o.Id == x.OrderId && o.SellerId != null) &&
                !db.SellerSettlements.Any(s => s.OrderId == x.OrderId))
            .OrderBy(x => x.Id).Take(50).ToListAsync(ct);
        var count = 0;
        foreach (var payment in payments)
        {
            await RecordSuccessfulPaymentAsync(payment.OrderId!.Value, payment.Id, ct);
            count++;
        }
        var refunds = await db.Refunds.AsNoTracking().Where(x => x.Status == "Succeeded" &&
                db.SellerSettlements.Any(s => s.OrderId == x.OrderId))
            .OrderBy(x => x.Id).Take(100).ToListAsync(ct);
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
        var due = await db.SellerSettlements
            .Where(x => x.Status == "Processing" && x.ReleaseAt <= DateTime.UtcNow && x.ProcessingAmount > 0 &&
                db.OrderTables.Any(o => o.Id == x.OrderId && (o.Status == "Delivered" || o.Status == "Closed")))
            .ToListAsync(ct);
        foreach (var settlement in due)
        {
            var account = await db.SellerAccounts.SingleAsync(x => x.Id == settlement.SellerAccountId, ct);
            var amount = settlement.ProcessingAmount;
            account.ProcessingBalance -= amount;
            account.AvailableBalance += amount;
            account.UpdatedAt = DateTime.UtcNow;
            settlement.ProcessingAmount = 0;
            settlement.Status = "Available";
            settlement.ReleasedAt = DateTime.UtcNow;
            AddEntry(account, settlement, "FundsReleased", "Available", amount, $"order:{settlement.OrderId}:released", "Hold period completed after delivery");
        }
        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    public async Task<SellerPayout> RequestPayoutAsync(int sellerId, decimal amount, string key, bool simulateFailure,
        CancellationToken ct = default)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new ArgumentException("Payout key is required", nameof(key));
        var account = await GetOrCreateAccountAsync(sellerId, ct);
        var existing = await db.SellerPayouts.SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            if (existing.SellerAccountId != account.Id || existing.Amount != decimal.Round(amount, 2) || existing.SimulateFailure != simulateFailure)
                throw new InvalidOperationException("Payout key belongs to a different request");
            return existing;
        }
        if (account.NegativeBalance > 0) throw new InvalidOperationException("Negative balance must be recovered before payout");
        if (account.AvailableBalance < amount) throw new InvalidOperationException("Available balance is insufficient");
        var now = DateTime.UtcNow;
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
        AddEntry(account, null, "Payout", "Available", -payout.Amount, $"payout:{payout.Id}:created", "Payout requested", payoutId: payout.Id);
        await db.SaveChangesAsync(ct);
        return payout;
    }

    public async Task<int> AdvancePayoutsAsync(CancellationToken ct = default)
    {
        var payouts = await db.SellerPayouts.Where(x => x.Status != "Completed" && x.Status != "Returned")
            .OrderBy(x => x.Id).Take(50).ToListAsync(ct);
        foreach (var payout in payouts)
        {
            var account = await db.SellerAccounts.SingleAsync(x => x.Id == payout.SellerAccountId, ct);
            payout.UpdatedAt = DateTime.UtcNow;
            if (payout.Status == "Created") payout.Status = "InProgress";
            else if (payout.Status == "InProgress")
            {
                payout.Status = "FundsSent";
                payout.BankReferenceId = "BANK-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            }
            else if (payout.SimulateFailure)
            {
                payout.Status = "Returned";
                payout.CompletedAt = DateTime.UtcNow;
                account.AvailableBalance += payout.Amount;
                account.UpdatedAt = DateTime.UtcNow;
                AddEntry(account, null, "PayoutReturned", "Available", payout.Amount, $"payout:{payout.Id}:returned", "Bank rejected payout; funds restored", payoutId: payout.Id);
            }
            else
            {
                payout.Status = "Completed";
                payout.CompletedAt = DateTime.UtcNow;
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
        var newLevel = metrics.CompletedOrders >= Level3Orders && metrics.PositiveFeedbackRate >= Level3Feedback && metrics.OnTimeDeliveryRate >= Level3OnTime ? 3
            : metrics.CompletedOrders >= Level2Orders && metrics.PositiveFeedbackRate >= Level2Feedback && metrics.OnTimeDeliveryRate >= Level2OnTime ? 2 : 1;
        ApplyLevel(account, newLevel);
        account.UpdatedAt = DateTime.UtcNow;
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
            _ => (3, Level3Orders, Level3Feedback, Level3OnTime)
        };
        var transactions = await db.FinancialTransactions.AsNoTracking().Where(x => x.SellerAccountId == account.Id && x.Type != "RefundApplied")
            .OrderByDescending(x => x.Id).Take(50)
            .Select(x => new FinanceTransactionView(x.Id, x.OrderId, x.Type, x.Bucket, x.Amount, x.Currency, x.Description, x.CreatedAt)).ToListAsync(ct);
        var payouts = await db.SellerPayouts.AsNoTracking().Where(x => x.SellerAccountId == account.Id)
            .OrderByDescending(x => x.Id).Take(20)
            .Select(x => new PayoutView(x.Id, x.Amount, x.Currency, x.Status, x.DestinationMasked, x.BankReferenceId, x.CreatedAt, x.CompletedAt)).ToListAsync(ct);
        return new SellerFinanceSummary(account.SellerId, account.Level, account.Level >= 3 ? "Instant" : $"Level {account.Level}", account.Status,
            account.MonthlySalesLimit, account.MonthlySalesAmount, account.HoldDays, account.ProcessingBalance, account.AvailableBalance,
            account.OnHoldBalance, account.NegativeBalance,
            new SellerLevelProgress(metrics.CompletedOrders, metrics.PositiveFeedbackRate, metrics.OnTimeDeliveryRate,
                target.Item1, target.Item2, target.Item3, target.Item4), transactions, payouts);
    }

    private async Task<SellerAccount> GetOrCreateAccountAsync(int sellerId, CancellationToken ct)
    {
        var account = await db.SellerAccounts.SingleOrDefaultAsync(x => x.SellerId == sellerId, ct);
        if (account is not null) return account;
        account = new SellerAccount { SellerId = sellerId, SalesMonth = MonthStart(), UpdatedAt = DateTime.UtcNow };
        ApplyLevel(account, 1);
        db.SellerAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account;
    }

    private void ApplyLevel(SellerAccount account, int level)
    {
        account.Level = level;
        account.MonthlySalesLimit = level switch { 1 => Level1Limit, 2 => Level2Limit, _ => Level3Limit };
        account.HoldDays = level switch { 1 => Level1HoldDays, 2 => Level2HoldDays, _ => 0 };
    }

    private void ResetMonth(SellerAccount account)
    {
        var month = MonthStart();
        if (account.SalesMonth >= month) return;
        account.SalesMonth = month;
        account.MonthlySalesAmount = 0m;
        account.UpdatedAt = DateTime.UtcNow;
    }

    private DateTime ResolveReleaseAt(DateTime now, int holdDays)
    {
        var seconds = configuration.GetValue<int?>("Finance:AcceleratedHoldSeconds");
        return seconds is > 0 ? now.AddSeconds(seconds.Value) : now.AddDays(holdDays);
    }

    private async Task<(int CompletedOrders, decimal PositiveFeedbackRate, decimal OnTimeDeliveryRate)> GetMetricsAsync(int sellerId, CancellationToken ct)
    {
        var completedOrders = await db.OrderTables.CountAsync(x => x.SellerId == sellerId && (x.Status == "Delivered" || x.Status == "Closed"), ct);
        var feedback = await db.Feedbacks.AsNoTracking().Where(x => x.SellerId == sellerId).Select(x => x.PositiveRate).FirstOrDefaultAsync(ct) ?? 0m;
        var delivered = await db.ShippingInfos.AsNoTracking().Where(x => x.Direction == "Outbound" && x.Status == "Delivered" &&
            db.OrderTables.Any(o => o.Id == x.OrderId && o.SellerId == sellerId)).Select(x => x.DeliveryAttempts).ToListAsync(ct);
        var onTime = delivered.Count == 0 ? 0m : decimal.Round(delivered.Count(x => x <= 1) * 100m / delivered.Count, 2);
        return (completedOrders, feedback, onTime);
    }

    private void AddEntry(SellerAccount account, SellerSettlement? settlement, string type, string bucket, decimal amount,
        string key, string description, int? refundId = null, int? payoutId = null)
    {
        db.FinancialTransactions.Add(new FinancialTransaction
        {
            SellerAccountId = account.Id,
            OrderId = settlement?.OrderId,
            SettlementId = settlement?.Id,
            RefundId = refundId,
            PayoutId = payoutId,
            Type = type,
            Bucket = bucket,
            Amount = decimal.Round(amount, 2),
            EntryKey = key,
            Description = description,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static DateTime MonthStart() => new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    private static decimal RemainingSellerProceeds(SellerSettlement settlement) =>
        Math.Max(0m, settlement.NetAmount - (settlement.RefundedAmount - settlement.FeeCreditAmount));
    private async Task<decimal> HeldAmountAsync(int settlementId, CancellationToken ct) =>
        await db.FinancialTransactions.Where(x => x.SettlementId == settlementId && x.Type == "FundHold")
            .Select(x => x.Amount).SingleOrDefaultAsync(ct);
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
