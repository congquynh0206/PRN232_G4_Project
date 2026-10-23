using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Finance;

// Read-only business projection: wallet transfers, holds, debt recovery and payouts are not sales.
public sealed class SellerFinanceReportService(ApplicationDbContext db, TimeProvider clock)
{
    private sealed record Entry(
        long Id,
        int? OrderId,
        int? RefundId,
        string Type,
        decimal Amount,
        DateTime At,
        decimal Recovery = 0m,
        decimal? CashRefund = null
    );

    private sealed class PeriodData(DateOnly period)
    {
        public DateOnly Period { get; } = period;
        public HashSet<int> Orders { get; } = [];
        public List<SellerReportSource> Sources { get; } = [];
        public decimal BuyerPaid,
            BuyerRefunded,
            PlatformFee,
            Net,
            PlatformSubsidy,
            SubsidyReversal,
            ShippingAdjustments,
            ReturnShippingCost,
            ReturnShippingRecovery;

        public void Add(Entry e)
        {
            Sources.Add(
                new(
                    e.Id,
                    e.OrderId,
                    e.RefundId,
                    e.Type,
                    e.Amount,
                    DateTime.SpecifyKind(e.At, DateTimeKind.Utc)
                )
            );
            Net += e.Amount;
            ReturnShippingRecovery += e.Recovery;
            switch (e.Type)
            {
                case "Sale":
                    BuyerPaid += e.Amount;
                    break;
                case "Refund":
                    BuyerRefunded += e.CashRefund ?? -e.Amount;
                    break;
                case "PlatformFee":
                case "FeeCredit":
                    PlatformFee -= e.Amount;
                    break;
                case "PlatformSubsidy":
                    PlatformSubsidy += e.Amount;
                    break;
                case "PlatformSubsidyReversal":
                    SubsidyReversal -= e.Amount;
                    break;
                case "ShippingAdjustmentCost":
                case "ShippingAdjustmentDebt":
                    ShippingAdjustments -= e.Amount;
                    break;
                case "ReturnShippingCost":
                    ReturnShippingCost -= e.Amount;
                    break;
            }
        }

        public SellerReportPeriod View(bool sources = false) =>
            new(
                Period,
                Orders.Count,
                BuyerPaid,
                BuyerRefunded,
                PlatformFee,
                Net,
                PlatformSubsidy,
                SubsidyReversal,
                ShippingAdjustments,
                ReturnShippingCost,
                ReturnShippingRecovery,
                sources
                    ? Sources
                        .OrderByDescending(x => x.OccurredAt)
                        .ThenByDescending(x => x.TransactionId)
                        .ToList()
                    : null
            );
    }

    public async Task<SellerFinanceReport> GetAsync(
        int sellerId,
        DateOnly? from,
        DateOnly? to,
        string? group,
        string? timeZone,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default
    )
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone ?? "UTC");
        }
        catch (TimeZoneNotFoundException)
        {
            throw new ArgumentException("Múi giờ không hợp lệ.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new ArgumentException("Múi giờ không hợp lệ.");
        }
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime
        );
        var startDate = from ?? new DateOnly(today.Year, today.Month, 1);
        var endDate = to ?? today;
        if (startDate.Year < 2 || endDate.Year >= 9999)
            throw new ArgumentException("Ngày báo cáo nằm ngoài khoảng hỗ trợ (năm 2–9998).");
        if (endDate < startDate || endDate.DayNumber - startDate.DayNumber > 365)
            throw new ArgumentException(
                "Khoảng ngày phải từ 1 đến 366 ngày và ngày bắt đầu không sau ngày kết thúc."
            );
        group = string.IsNullOrWhiteSpace(group) ? "day" : group.ToLowerInvariant();
        if (group is not ("day" or "week" or "month"))
            throw new ArgumentException("Nhóm kỳ không hợp lệ.");
        if (group == "day" && endDate.DayNumber - startDate.DayNumber > 92)
            throw new ArgumentException("Khoảng trên 93 ngày hãy nhóm theo tuần hoặc tháng.");
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startDate.ToDateTime(TimeOnly.MinValue), zone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(
            endDate.AddDays(1).ToDateTime(TimeOnly.MinValue),
            zone
        );

        // Backfilled rows belong to PaidAt/CompletedAt, not their late ledger insertion time.
        var paid = await (
            from e in db.FinancialTransactions.AsNoTracking()
            join s in db.SellerSettlements.AsNoTracking() on e.SettlementId equals (int?)s.Id
            join p in db.Payments.AsNoTracking() on s.PaymentId equals p.Id
            where
                s.SellerId == sellerId
                && e.SellerAccountId == s.SellerAccountId
                && db.SellerAccounts.Any(a => a.Id == s.SellerAccountId && a.SellerId == sellerId)
                && p.OrderId == s.OrderId
                && p.Status == "Succeeded"
                && p.PaidAt >= startUtc
                && p.PaidAt < endUtc
                && (e.Type == "Sale" || e.Type == "PlatformSubsidy" || e.Type == "PlatformFee")
            select new
            {
                e.Id,
                e.OrderId,
                e.RefundId,
                e.Type,
                e.Amount,
                At = p.PaidAt!.Value,
            }
        ).ToListAsync(ct);
        var refunded = await (
            from e in db.FinancialTransactions.AsNoTracking()
            join r in db.Refunds.AsNoTracking() on e.RefundId equals (int?)r.Id
            join s in db.SellerSettlements.AsNoTracking() on r.OrderId equals s.OrderId
            where
                s.SellerId == sellerId
                && e.SellerAccountId == s.SellerAccountId
                && db.SellerAccounts.Any(a => a.Id == s.SellerAccountId && a.SellerId == sellerId)
                && r.PaymentId == s.PaymentId
                && r.Status == "Succeeded"
                && r.CompletedAt >= startUtc
                && r.CompletedAt < endUtc
                && (
                    e.Type == "Refund"
                    || e.Type == "PlatformSubsidyReversal"
                    || e.Type == "FeeCredit"
                )
            select new
            {
                e.Id,
                e.OrderId,
                e.RefundId,
                e.Type,
                e.Amount,
                CashRefund = r.Amount,
                r.BuyerReturnShippingRecovery,
                At = r.CompletedAt!.Value,
            }
        ).ToListAsync(ct);
        var costs = await (
            from e in db.FinancialTransactions.AsNoTracking()
            join a in db.SellerAccounts.AsNoTracking() on e.SellerAccountId equals a.Id
            where
                a.SellerId == sellerId
                && e.CreatedAt >= startUtc
                && e.CreatedAt < endUtc
                && (
                    e.Type == "ReturnShippingCost"
                    || e.Type == "ShippingAdjustmentCost"
                    || e.Type == "NegativeBalance"
                        && e.EntryKey.StartsWith("address-change:")
                        && e.EntryKey.EndsWith(":negative")
                )
            select new
            {
                e.Id,
                e.OrderId,
                e.RefundId,
                e.Type,
                e.Amount,
                At = e.CreatedAt,
            }
        ).ToListAsync(ct);

        DateOnly Key(DateOnly date) =>
            group switch
            {
                "week" => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
                "month" => new DateOnly(date.Year, date.Month, 1),
                _ => date,
            };
        var first = Key(startDate);
        var periods = new SortedDictionary<DateOnly, PeriodData>();
        for (
            var date = first;
            date <= endDate;
            date = group switch
                {
                    "week" => date.AddDays(7),
                    "month" => date.AddMonths(1),
                    _ => date.AddDays(1),
                }
        )
            periods.Add(date, new PeriodData(date));
        void Add(
            long id,
            int? orderId,
            int? refundId,
            string type,
            decimal amount,
            DateTime at,
            decimal recovery = 0m,
            decimal? cashRefund = null
        )
        {
            var day = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(at, DateTimeKind.Utc), zone)
            );
            periods[Key(day)]
                .Add(
                    new Entry(
                        id,
                        orderId,
                        refundId,
                        type == "NegativeBalance" ? "ShippingAdjustmentDebt" : type,
                        amount,
                        at,
                        recovery,
                        cashRefund
                    )
                );
        }
        foreach (var e in paid)
            Add(e.Id, e.OrderId, e.RefundId, e.Type, e.Amount, e.At);
        foreach (var e in refunded)
            Add(
                e.Id,
                e.OrderId,
                e.RefundId,
                e.Type,
                e.Amount,
                e.At,
                e.Type == "Refund" ? e.BuyerReturnShippingRecovery : 0m,
                e.Type == "Refund" ? e.CashRefund : null
            );
        foreach (var e in costs)
            Add(e.Id, e.OrderId, e.RefundId, e.Type, e.Amount, e.At);
        var orders = await (
            from s in db.SellerSettlements.AsNoTracking()
            join p in db.Payments.AsNoTracking() on s.PaymentId equals p.Id
            where
                s.SellerId == sellerId
                && db.SellerAccounts.Any(a => a.Id == s.SellerAccountId && a.SellerId == sellerId)
                && p.OrderId == s.OrderId
                && p.Status == "Succeeded"
                && p.PaidAt >= startUtc
                && p.PaidAt < endUtc
            select new { s.OrderId, At = p.PaidAt!.Value }
        ).ToListAsync(ct);
        foreach (var o in orders)
        {
            var day = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(o.At, DateTimeKind.Utc), zone)
            );
            periods[Key(day)].Orders.Add(o.OrderId);
        }
        var missingPayments = await db
            .Payments.AsNoTracking()
            .CountAsync(
                p =>
                    p.Status == "Succeeded"
                    && p.PaidAt >= startUtc
                    && p.PaidAt < endUtc
                    && p.OrderId != null
                    && db.OrderTables.Any(o => o.Id == p.OrderId && o.SellerId == sellerId)
                    && !db.SellerSettlements.Any(s =>
                        s.PaymentId == p.Id
                        && s.OrderId == p.OrderId
                        && s.SellerId == sellerId
                        && db.SellerAccounts.Any(a =>
                            a.Id == s.SellerAccountId && a.SellerId == sellerId
                        )
                        && db.FinancialTransactions.Any(e =>
                            e.SettlementId == s.Id
                            && e.SellerAccountId == s.SellerAccountId
                            && e.Type == "Sale"
                        )
                    ),
                ct
            );
        var missingRefunds = await db
            .Refunds.AsNoTracking()
            .CountAsync(
                r =>
                    r.Status == "Succeeded"
                    && r.CompletedAt >= startUtc
                    && r.CompletedAt < endUtc
                    && db.SellerSettlements.Any(s =>
                        s.OrderId == r.OrderId && s.SellerId == sellerId
                    )
                    && !db.SellerSettlements.Any(s =>
                        s.OrderId == r.OrderId
                        && s.PaymentId == r.PaymentId
                        && s.SellerId == sellerId
                        && db.SellerAccounts.Any(a =>
                            a.Id == s.SellerAccountId && a.SellerId == sellerId
                        )
                        && db.FinancialTransactions.Any(e =>
                            e.RefundId == r.Id
                            && e.SellerAccountId == s.SellerAccountId
                            && e.Type == "RefundApplied"
                        )
                        && db.FinancialTransactions.Any(e =>
                            e.RefundId == r.Id
                            && e.SellerAccountId == s.SellerAccountId
                            && e.Type == "Refund"
                        )
                    ),
                ct
            );
        var total = new PeriodData(first);
        foreach (var p in periods.Values)
        {
            total.Orders.UnionWith(p.Orders);
            total.BuyerPaid += p.BuyerPaid;
            total.BuyerRefunded += p.BuyerRefunded;
            total.PlatformFee += p.PlatformFee;
            total.Net += p.Net;
            total.PlatformSubsidy += p.PlatformSubsidy;
            total.SubsidyReversal += p.SubsidyReversal;
            total.ShippingAdjustments += p.ShippingAdjustments;
            total.ReturnShippingCost += p.ReturnShippingCost;
            total.ReturnShippingRecovery += p.ReturnShippingRecovery;
        }
        var all = periods.Values.ToList();
        page = Math.Min(page, Math.Max(1, (all.Count + pageSize - 1) / pageSize));
        return new(
            startDate,
            endDate,
            zone.Id,
            group,
            total.View(),
            all.Select(x => x.View()).ToList(),
            page,
            pageSize,
            all.Count,
            all.Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.View(true)).ToList(),
            missingPayments,
            missingRefunds
        );
    }
}
