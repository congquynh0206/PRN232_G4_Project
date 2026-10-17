using System.Data;
using System.Net;
using G4.Contracts.Checkout;
using G4.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Finance;
public sealed partial class SellerFinanceService
{
    private bool IsDebtOverdue(SellerAccount a) => a.NegativeBalance > 0 && a.DebtDueAt <= Now;
    private (int Due, int Warning) DebtPolicy()
    {
        var due = configuration.GetValue("Finance:Debt:DueSeconds", 7 * 86400);
        var warning = configuration.GetValue("Finance:Debt:WarningBeforeSeconds", 86400);
        if (due <= 0 || warning <= 0 || warning >= due)
            throw new InvalidOperationException("Debt policy requires 0 < WarningBeforeSeconds < DueSeconds");
        return (due, warning);
    }

    // All callers own the seller finance transaction gate before reading or changing debt.
    private async Task SyncDebtAsync(SellerAccount account, CancellationToken ct)
    {
        if (account.NegativeBalance < 0) throw new InvalidOperationException("Seller debt cannot be negative");
        if (account.NegativeBalance == 0 && account.DebtStartedAt == null) return;
        SellerDebtCycle cycle;
        if (account.DebtStartedAt == null)
        {
            var policy = DebtPolicy();
            account.DebtCycleNumber++;
            account.DebtStartedAt = Now;
            account.DebtDueAt = Now.AddSeconds(policy.Due);
            account.DebtSuspended = false;
            cycle = new SellerDebtCycle { SellerAccountId = account.Id, Number = account.DebtCycleNumber,
                OpeningBalance = account.NegativeBalance, StartedAt = Now, DueAt = account.DebtDueAt.Value,
                WarningAt = account.DebtDueAt.Value.AddSeconds(-policy.Warning) };
            db.SellerDebtCycles.Add(cycle);
            await DebtEventAsync(account, "DebtStarted", "Phát sinh kỳ nợ. Doanh thu sau phí sẽ bù nợ; chưa thể rút tiền.", ct);
        }
        else
            cycle = db.SellerDebtCycles.Local.FirstOrDefault(x => x.SellerAccountId == account.Id && x.Number == account.DebtCycleNumber)
                ?? await db.SellerDebtCycles.SingleAsync(x => x.SellerAccountId == account.Id && x.Number == account.DebtCycleNumber, ct);

        await ObserveDebtRecoveryAsync(account, cycle, ct);

        if (account.NegativeBalance == 0)
        {
            var wasSuspended = account.DebtSuspended;
            cycle.ClosedAt = Now;
            account.DebtStartedAt = null;
            account.DebtDueAt = null;
            account.DebtSuspended = false;
            await DebtEventAsync(account, "DebtCleared", account.PermanentSalesBlockedAt != null
                ? "Đã trả hết nợ; quyết định khóa bán vĩnh viễn vẫn giữ nguyên."
                : wasSuspended
                ? "Đã trả hết nợ và gỡ đình chỉ do nợ. Các hạn chế khác vẫn theo quyết định hiện có."
                : "Đã trả hết nợ và kết thúc kỳ nợ.", ct, notify: !wasSuspended);
            if (wasSuspended && account.PermanentSalesBlockedAt == null)
                await DebtEventAsync(account, "DebtRestored", "Đã gỡ hạn chế bán hàng do nợ.", ct);
        }
        else if (Now >= cycle.DueAt)
        {
            if (!account.DebtSuspended)
            {
                account.DebtSuspended = true;
                cycle.SuspendedAt = Now;
                await DebtEventAsync(account, "DebtOverdue", "Nợ quá hạn: đình chỉ đăng bán và nhận đơn mới. Bạn vẫn có thể trả nợ và xử lý đơn hiện có.", ct);
            }
        }
        else if (Now >= cycle.WarningAt && cycle.WarnedAt == null)
        {
            cycle.WarnedAt = Now;
            await DebtEventAsync(account, "DebtWarning", "Khoản nợ sắp đến hạn. Hãy trả hết để tránh đình chỉ bán hàng.", ct);
        }
    }

    private async Task DebtEventAsync(SellerAccount account, string type, string description, CancellationToken ct, string? suffix = null, bool notify = true)
    {
        var key = suffix ?? $"debt:{account.Id}:{account.DebtCycleNumber}:{type}";
        db.SellerDebtEvents.Add(new SellerDebtEvent { SellerAccountId = account.Id, CycleNumber = account.DebtCycleNumber,
            EventType = type, EntryKey = key, Balance = account.NegativeBalance, Description = description, CreatedAt = Now });
        if (!notify) return;
        var email = await db.Users.Where(x => x.Id == account.SellerId).Select(x => x.Email).SingleOrDefaultAsync(ct);
        var valid = NotificationService.IsAddress(email);
        var body = $"{description}\nSố nợ: {account.NegativeBalance:0.00} USD"
            + (account.DebtDueAt is { } due ? $"\nHạn trả (UTC): {due:yyyy-MM-dd HH:mm:ss}" : "");
        var subject = type switch
        {
            "DebtStarted" => "Phát sinh khoản nợ người bán",
            "DebtWarning" => "Khoản nợ sắp đến hạn",
            "DebtOverdue" => "Đình chỉ bán hàng do nợ quá hạn",
            "DebtRepaymentSucceeded" => "Trả nợ thành công",
            "DebtRepaymentFailed" => "Ngân hàng từ chối trả nợ",
            "DebtCleared" => "Đã trả hết khoản nợ",
            "DebtRestored" => "Đã gỡ đình chỉ bán hàng do nợ",
            "DebtWriteOff" => "Ghi nhận nợ khó thu và khóa bán vĩnh viễn",
            "WriteOffRecovery" => "Thu hồi nợ sau quyết định nợ khó thu",
            _ => "Cập nhật khoản nợ người bán"
        };
        db.NotificationOutbox.Add(new NotificationOutbox { SellerId = account.SellerId, EventKey = key, EventType = type,
            Recipient = email ?? "", From = configuration["Mail:From"] ?? "g4@example.test", Subject = subject,
            Body = body, HtmlBody = $"<div style='white-space:pre-wrap'>{WebUtility.HtmlEncode(body)}</div>",
            Status = valid ? "Pending" : "Failed", LastErrorCode = valid ? null : "RecipientInvalid", CreatedAt = Now });
    }

    private SellerDebtSummary DebtView(SellerAccount a)
    {
        var cycle = db.SellerDebtCycles.Local.FirstOrDefault(c => c.SellerAccountId == a.Id && c.Number == a.DebtCycleNumber);
        DateTime? earliest = null;
        if (configuration.GetValue("Finance:WriteOff:Enabled", true) && a.NegativeBalance > 0 && a.PermanentSalesBlockedAt == null
            && cycle?.WriteOffEligibleAt is { } eligible && cycle.LastRecoveryAt is { } recovery)
            earliest = new[] { eligible, cycle.DueAt, recovery.AddSeconds(cycle.RecoveryGraceSeconds) }.Max();
        return new(a.SellerId, a.NegativeBalance, a.DebtCycleNumber,
            a.DebtStartedAt, a.DebtDueAt, a.DebtStartedAt is { } start ? Math.Max(0, (long)(Now - start).TotalSeconds) : 0,
            IsDebtOverdue(a), a.DebtSuspended, a.Status, a.PermanentSalesBlockedAt, earliest, cycle?.RecoveryGraceSeconds ?? 0);
    }

    public async Task<SellerDebtSummary> GetDebtAsync(int sellerId, CancellationToken ct = default)
    {
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
        await AccountGateAsync(sellerId, ct);
        var a = await GetOrCreateAccountAsync(sellerId, ct);
        await LockAccountAsync(a, ct);
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return DebtView(a);
    }

    public async Task ValidateNewSaleAsync(int sellerId, CancellationToken ct = default)
    {
        var debt = await GetDebtAsync(sellerId, ct);
        if (debt.PermanentSalesBlockedAt != null) throw new InvalidOperationException("Tài khoản đã bị khóa bán vĩnh viễn; vẫn được trả nợ và xử lý đơn cũ.");
        if (debt.AccountStatus != "Active") throw new InvalidOperationException("Seller account is restricted");
        if (debt.Overdue) throw new InvalidOperationException("Seller has overdue debt and cannot receive new orders or publish products");
    }

    public async Task<int> MaintainDebtsAsync(CancellationToken ct = default)
    {
        // Only select work that is due: closed/recent debt must not starve other sellers.
        var now = Now;
        var ids = await db.SellerAccounts.AsNoTracking().Where(a =>
            a.NegativeBalance > 0 && a.DebtStartedAt == null ||
            a.NegativeBalance == 0 && a.DebtStartedAt != null ||
            a.NegativeBalance > 0 && !a.DebtSuspended &&
                (a.DebtDueAt <= now || db.SellerDebtCycles.Any(c => c.SellerAccountId == a.Id && c.Number == a.DebtCycleNumber
                    && c.WarnedAt == null && c.WarningAt <= now)))
            .OrderBy(a => a.Id).Select(a => a.SellerId).Take(100).ToListAsync(ct);
        foreach (var id in ids) await GetDebtAsync(id, ct);
        return ids.Count;
    }

    public async Task<DebtRepaymentView> RepayDebtAsync(int sellerId, DebtRepaymentRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0 || Money(request.Amount) != request.Amount || request.ExpectedDebt <= 0
            || Money(request.ExpectedDebt) != request.ExpectedDebt || request.Amount > request.ExpectedDebt)
            throw new ArgumentException("Số tiền trả nợ cần lớn hơn 0, tối đa 2 chữ số thập phân và không vượt số nợ.");
        if (string.IsNullOrWhiteSpace(request.Key) || request.Key.Length > 100)
            throw new ArgumentException("Mã yêu cầu trả nợ cần từ 1 đến 100 ký tự.");
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
        await AccountGateAsync(sellerId, ct);
        var a = await GetOrCreateAccountAsync(sellerId, ct);
        var existing = await db.SellerDebtRepayments.SingleOrDefaultAsync(x => x.SellerAccountId == a.Id && x.RequestKey == request.Key, ct);
        if (existing != null)
        {
            if (existing.Amount != request.Amount || existing.ExpectedDebt != request.ExpectedDebt
                || existing.CycleNumber != request.ExpectedCycleNumber || existing.SimulateFailure != request.SimulateFailure)
                throw new InvalidOperationException("Mã trả nợ đã được dùng cho nội dung khác.");
            if (tx != null) await tx.CommitAsync(ct);
            return RepaymentView(existing);
        }
        await LockAccountAsync(a, ct);
        if (a.NegativeBalance != request.ExpectedDebt || a.DebtCycleNumber != request.ExpectedCycleNumber)
            throw new InvalidOperationException($"Khoản nợ đã thay đổi; số nợ hiện tại {a.NegativeBalance:0.00} USD. Hãy tải lại và xác nhận số tiền.");
        var repayment = new SellerDebtRepayment { SellerAccountId = a.Id, CycleNumber = a.DebtCycleNumber,
            Amount = request.Amount, ExpectedDebt = request.ExpectedDebt, RequestKey = request.Key,
            SimulateFailure = request.SimulateFailure, Status = request.SimulateFailure ? "Rejected" : "Succeeded",
            BankReferenceId = "DEBT-BANK-" + Guid.NewGuid().ToString("N").ToUpperInvariant(), CreatedAt = Now };
        if (!request.SimulateFailure) a.NegativeBalance -= request.Amount;
        repayment.RemainingDebt = a.NegativeBalance;
        db.SellerDebtRepayments.Add(repayment);
        await db.SaveChangesAsync(ct);
        if (!request.SimulateFailure)
            AddEntry(a, null, "DebtRepayment", "Negative", -request.Amount,
                $"debt-repayment:{repayment.Id}", $"Bank repayment {repayment.BankReferenceId}");
        await DebtEventAsync(a, request.SimulateFailure ? "DebtRepaymentFailed" : "DebtRepaymentSucceeded",
            $"Ngân hàng giả lập {(request.SimulateFailure ? "từ chối" : "xác nhận")} trả {request.Amount:0.00} USD. Mã {repayment.BankReferenceId}.",
            ct, $"debt-repayment:{repayment.Id}:notification");
        a.UpdatedAt = Now;
        await SyncDebtAsync(a, ct);
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return RepaymentView(repayment);
    }

    private static DebtRepaymentView RepaymentView(SellerDebtRepayment r) => new(r.Id, r.CycleNumber, r.Amount,
        r.RemainingDebt, r.Currency, r.Status, r.BankReferenceId, r.CreatedAt);

    private void RecoverAvailableDebt(SellerAccount a, string key)
    {
        var recovered = Math.Min(a.NegativeBalance, a.AvailableBalance);
        if (recovered <= 0) return;
        a.AvailableBalance -= recovered;
        a.NegativeBalance -= recovered;
        AddEntry(a, null, "DebtRecovery", "Negative", -recovered, key, "Wallet credit recovered seller debt");
    }
}
