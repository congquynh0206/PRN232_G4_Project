using System.Data;
using G4.Infrastructure.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Finance;
public sealed partial class SellerFinanceService
{
    private (int After, int Inactive) WriteOffPolicy()
    {
        var after = configuration.GetValue("Finance:WriteOff:AfterSeconds", 30 * 86400);
        var inactive = configuration.GetValue("Finance:WriteOff:InactivitySeconds", 7 * 86400);
        if (after <= configuration.GetValue("Finance:Debt:DueSeconds", 7 * 86400) || inactive <= 0 || inactive > after)
            throw new InvalidOperationException("Write-off cần AfterSeconds lớn hơn hạn nợ và 0 < InactivitySeconds <= AfterSeconds.");
        return (after, inactive);
    }
    private async Task ObserveDebtRecoveryAsync(SellerAccount account, SellerDebtCycle cycle, CancellationToken ct)
    {
        if (cycle.WriteOffEligibleAt == null)
        {
            var policy = WriteOffPolicy();
            cycle.WriteOffEligibleAt = cycle.StartedAt.AddSeconds(policy.After);
            cycle.RecoveryGraceSeconds = policy.Inactive;
            // Existing cycles get a full inactivity grace after upgrade, without changing their original due date.
            cycle.LastRecoveryAt = Now;
            cycle.LastObservedBalance = account.NegativeBalance;
            return;
        }
        var recovered = Math.Max(0m, cycle.LastObservedBalance - account.NegativeBalance);
        cycle.LastObservedBalance = account.NegativeBalance;
        if (recovered <= 0) return;
        cycle.LastRecoveryAt = Now;
        var decision = await db.SellerDebtWriteOffs.SingleOrDefaultAsync(x => x.SellerAccountId == account.Id && x.CycleNumber == cycle.Number, ct);
        if (decision == null) return;
        var already = await db.SellerWriteOffRecoveries.Where(x => x.WriteOffId == decision.Id).SumAsync(x => x.Amount, ct)
            + db.SellerWriteOffRecoveries.Local.Where(x => x.WriteOffId == decision.Id && db.Entry(x).State == EntityState.Added).Sum(x => x.Amount);
        var allocated = Math.Min(recovered, Math.Max(0m, decision.OriginalAmount - already));
        if (allocated <= 0) return;
        db.SellerWriteOffRecoveries.Add(new SellerWriteOffRecovery { WriteOffId = decision.Id, Amount = allocated, CreatedAt = Now });
        await DebtEventAsync(account, "WriteOffRecovery", $"Đã thu hồi {allocated:0.00} USD sau write-off #{decision.Id}. Khóa bán vĩnh viễn giữ nguyên.",
            ct, $"write-off:{decision.Id}:recovery:{Guid.NewGuid():N}");
    }

    public async Task<int> MaintainWriteOffsAsync(CancellationToken ct = default)
    {
        if (!configuration.GetValue("Finance:WriteOff:Enabled", true)) return 0;
        var now = Now;
        var candidates = await db.SellerAccounts.AsNoTracking().Where(a => a.NegativeBalance > 0 && a.DebtStartedAt != null
            && a.PermanentSalesBlockedAt == null && db.SellerDebtCycles.Any(c => c.SellerAccountId == a.Id && c.Number == a.DebtCycleNumber
                && (c.WriteOffEligibleAt == null || c.WriteOffEligibleAt <= now && c.DueAt <= now
                    && c.LastRecoveryAt != null && c.LastRecoveryAt.Value.AddSeconds(c.RecoveryGraceSeconds) <= now)))
            .OrderBy(a => a.Id).Select(a => a.SellerId).Take(100).ToListAsync(ct);
        var count = 0;
        foreach (var sellerId in candidates)
        {
            await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
            await AccountGateAsync(sellerId, ct);
            var account = await db.SellerAccounts.SingleAsync(a => a.SellerId == sellerId, ct);
            await LockAccountAsync(account, ct);
            if (account.NegativeBalance <= 0 || account.PermanentSalesBlockedAt != null || account.DebtStartedAt == null)
            {
                await db.SaveChangesAsync(ct); if (tx != null) await tx.CommitAsync(ct); continue;
            }
            var cycle = db.SellerDebtCycles.Local.Single(c => c.SellerAccountId == account.Id && c.Number == account.DebtCycleNumber);
            if (cycle.WriteOffEligibleAt > Now || cycle.DueAt > Now || cycle.LastRecoveryAt!.Value.AddSeconds(cycle.RecoveryGraceSeconds) > Now)
            {
                await db.SaveChangesAsync(ct); if (tx != null) await tx.CommitAsync(ct); continue;
            }
            // Same gate order as identity changes: finance account, then identity registry.
            await SellerRegistryGate.LockAsync(db, ct);
            var identities = await db.SellerIdentities.AsNoTracking().Where(i => i.SellerId == sellerId).ToListAsync(ct);
            if (identities.Count > 0) await new SellerIdentityHasher(configuration).EnsureKeyAsync(db, ct);
            var decision = new SellerDebtWriteOff { SellerAccountId = account.Id, CycleNumber = cycle.Number,
                OriginalAmount = account.NegativeBalance, DebtStartedAt = cycle.StartedAt, DebtDueAt = cycle.DueAt,
                EligibleAt = cycle.WriteOffEligibleAt!.Value, LastRecoveryAt = cycle.LastRecoveryAt.Value,
                RecoveryGraceSeconds = cycle.RecoveryGraceSeconds, BlacklistedIdentities = identities.Count,
                Reason = $"Nợ đã đủ tuổi write-off, đã quá hạn và không có khoản thu hồi trong {cycle.RecoveryGraceSeconds} giây gần nhất.",
                CreatedAt = Now };
            db.SellerDebtWriteOffs.Add(decision);
            account.PermanentSalesBlockedAt = Now; account.UpdatedAt = Now;
            await db.SaveChangesAsync(ct);
            foreach (var identity in identities)
            {
                if (await db.SellerBlacklist.AnyAsync(x => x.KeyId == identity.KeyId && x.Fingerprint == identity.Fingerprint, ct)) continue;
                db.SellerBlacklist.Add(new SellerBlacklistEntry { Kind = identity.Kind, MaskedValue = identity.MaskedValue,
                    Fingerprint = identity.Fingerprint, KeyId = identity.KeyId, SourceSellerId = sellerId, WriteOffId = decision.Id,
                    Reason = $"Quyết định nợ khó thu tự động #{decision.Id}", CreatedAt = Now });
            }
            AddEntry(account, null, "DebtWriteOff", "Audit", 0m, $"write-off:{decision.Id}", $"Automatic write-off decision #{decision.Id}; original debt preserved");
            await DebtEventAsync(account, "DebtWriteOff", $"Hệ thống ghi nhận {decision.OriginalAmount:0.00} USD nợ khó thu và khóa bán vĩnh viễn. "
                + (identities.Count == 0 ? "Chưa có định danh đã xác minh để đối chiếu blacklist. " : "Định danh đã xác minh được đưa vào blacklist. ")
                + "Bạn vẫn đăng nhập, trả nợ và xử lý đơn cũ.", ct);
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            count++;
        }
        return count;
    }
}
