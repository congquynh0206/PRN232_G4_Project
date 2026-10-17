using System.Text.Json;
using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Finance;
public sealed partial class SellerFinanceService
{
    private static readonly JsonSerializerOptions LevelJson = new(JsonSerializerDefaults.Web);

    private async Task<SellerLevelMetrics> ReadLevelMetricsAsync(SellerAccount account, SellerLevelPolicy policy, CancellationToken ct)
    {
        var now = Now; var start = now.AddDays(-policy.WindowDays);
        var paid = db.Payments.Where(x => x.Status == "Succeeded" && (x.PaidAt ?? x.CreatedAt) <= now);
        var cohort = db.OrderTables.Where(o => o.SellerId == account.SellerId
            && paid.Any(p => p.OrderId == o.Id && (p.PaidAt ?? p.CreatedAt) >= start));
        var count = await cohort.CountAsync(ct);
        var completed = await db.OrderTables.CountAsync(o => o.SellerId == account.SellerId && (o.Status == "Delivered" || o.Status == "Closed"
            || db.ShippingInfos.Any(s => s.OrderId == o.Id && s.Direction == "Outbound" && s.DeliveredAt != null && s.DeliveredAt <= now))
            && paid.Any(p => p.OrderId == o.Id), ct);
        var feedback = await db.SellerOrderFeedbacks.AsNoTracking().Where(x => x.SellerId == account.SellerId && x.CreatedAt <= now
            && cohort.Any(o => o.Id == x.OrderId)).Select(x => x.Rating).ToListAsync(ct);
        var deliveries = await db.ShippingInfos.AsNoTracking().Where(x => x.Direction == "Outbound"
            && x.Status == "Delivered" && x.DeliveredAt != null && x.DeliveredAt <= now && x.EstimatedArrival != null
            && cohort.Any(o => o.Id == x.OrderId)
            && !db.ShippingInfos.Any(later => later.OrderId == x.OrderId && later.Direction == "Outbound" && later.Id > x.Id))
            .Select(x => new { x.DeliveredAt, x.EstimatedArrival }).ToListAsync(ct);
        var cancelled = await cohort.CountAsync(o => o.Status == "Cancelled"
            || db.Refunds.Any(r => r.OrderId == o.Id && r.Status == "Succeeded" && r.Reason == "cancel"), ct);
        var returned = await cohort.CountAsync(o => db.ReturnRequests.Any(r => r.OrderId == o.Id
            && (r.Status == "Approved" || r.Status == "ReturnShipping" || r.Status == "ReceivedBySeller"
                || r.Status == "RefundPending" || r.Status == "RefundFailed" || r.Status == "Refunded")), ct);
        var disputes = await cohort.CountAsync(o => db.Disputes.Any(d => d.OrderId == o.Id && d.WorkflowEnabled), ct);
        var registered = await db.SellerRegistrations.Where(x => x.SellerId == account.SellerId).Select(x => (DateTime?)x.CreatedAt).FirstOrDefaultAsync(ct);
        var firstPayment = await paid.Where(p => db.OrderTables.Any(o => o.Id == p.OrderId && o.SellerId == account.SellerId))
            .Select(x => (DateTime?)(x.PaidAt ?? x.CreatedAt)).MinAsync(ct);
        var activityStart = new[] { registered, firstPayment, account.LevelActivityStartedAt }.Where(x => x != null).Min() ?? now;
        static decimal Rate(int n, int total) => total == 0 ? 0 : n * 100m / total;
        return new(completed, count, feedback.Count, feedback.Count == 0 ? null : Rate(feedback.Count(x => x >= 4), feedback.Count),
            deliveries.Count, deliveries.Count == 0 ? null : Rate(deliveries.Count(x => x.DeliveredAt <= x.EstimatedArrival), deliveries.Count),
            Rate(cancelled, count), Rate(returned, count), Rate(disputes, count), Math.Max(0, (int)(now - activityStart).TotalDays),
            account.NegativeBalance, account.Status != "Active" || account.DebtSuspended || account.PermanentSalesBlockedAt != null, start, now);
    }

    private async Task EvaluateAndSaveLevelAsync(SellerAccount account, SellerLevelMetrics measured, SellerLevelPolicy policy, string source, CancellationToken ct)
    {
        account.LevelActivityStartedAt ??= Now;
        // Order metrics are read before taking the finance gate. Payment paths lock
        // orders before this gate; reading orders while holding it inverts that order.
        // Debt/restriction and the current level are always rechecked under the gate.
        var metrics = measured with { NegativeBalance = account.NegativeBalance,
            Restricted = account.Status != "Active" || account.DebtSuspended || account.PermanentSalesBlockedAt != null };
        var decision = SellerLevelRules.Decide(account.Level, metrics, policy);
        ApplyLevel(account, decision.ResultLevel);
        account.LastLevelEvaluatedAt = Now; account.UpdatedAt = Now;
        db.SellerLevelEvaluations.Add(new SellerLevelEvaluation { SellerId = account.SellerId,
            PreviousLevel = decision.PreviousLevel, ResultLevel = decision.ResultLevel, Source = source,
            Reason = decision.Reason, SnapshotJson = JsonSerializer.Serialize(decision, LevelJson), CreatedAt = Now });
        await db.SaveChangesAsync(ct);
    }

    public async Task<SellerLevelDetails> GetLevelDetailsAsync(int sellerId, CancellationToken ct = default)
    {
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await AccountGateAsync(sellerId, ct);
        var account = await GetOrCreateAccountAsync(sellerId, ct); await LockAccountAsync(account, ct);
        await db.SaveChangesAsync(ct);
        if (tx != null) { await tx.CommitAsync(ct); await tx.DisposeAsync(); }
        var policy = SellerLevelRules.Policy(configuration);
        var metrics = await ReadLevelMetricsAsync(account, policy, ct);
        var history = await db.SellerLevelEvaluations.AsNoTracking().Where(x => x.SellerId == sellerId).OrderByDescending(x => x.Id).Take(20).ToListAsync(ct);
        var result = new SellerLevelDetails(sellerId, account.Level, account.LastLevelEvaluatedAt,
            account.LastLevelEvaluatedAt?.AddSeconds(policy.EvaluationSeconds), SellerLevelRules.Decide(account.Level, metrics, policy),
            history.Select(x => new SellerLevelHistory(x.Id, x.Source, x.CreatedAt, JsonSerializer.Deserialize<SellerLevelDecision>(x.SnapshotJson, LevelJson)!)).ToArray());
        return result;
    }

    public async Task<int> MaintainLevelsAsync(CancellationToken ct = default)
    {
        var policy = SellerLevelRules.Policy(configuration); var cutoff = Now.AddSeconds(-policy.EvaluationSeconds);
        var ids = await db.Users.AsNoTracking().Where(u => u.Role == "seller"
            && !db.SellerAccounts.Any(a => a.SellerId == u.Id && a.LastLevelEvaluatedAt != null && a.LastLevelEvaluatedAt > cutoff))
            .OrderBy(u => db.SellerAccounts.Where(a => a.SellerId == u.Id).Select(a => a.LastLevelEvaluatedAt).FirstOrDefault())
            .ThenBy(u => u.Id).Select(u => u.Id).Take(50).ToListAsync(ct);
        var count = 0;
        foreach (var id in ids)
        {
            var observed = await db.SellerAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.SellerId == id, ct) ?? new SellerAccount { SellerId = id };
            var metrics = await ReadLevelMetricsAsync(observed, policy, ct);
            await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
            await AccountGateAsync(id, ct);
            var account = await GetOrCreateAccountAsync(id, ct); await LockAccountAsync(account, ct);
            if (account.LastLevelEvaluatedAt == null || account.LastLevelEvaluatedAt <= cutoff)
            {
                await EvaluateAndSaveLevelAsync(account, metrics, policy, "Worker", ct); count++;
            }
            if (tx != null) await tx.CommitAsync(ct);
        }
        return count;
    }
}
