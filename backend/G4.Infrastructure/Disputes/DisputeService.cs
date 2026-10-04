using System.Data;
using System.Text.Json;
using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace G4.Infrastructure.Disputes;

public sealed class DisputeService(ApplicationDbContext db, ISellerFinanceService finance,
    IReturnService returns, IConfiguration config, TimeProvider clock, ILogger<DisputeService>? logger = null) : IDisputeService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateTime SellerDeadline
    {
        get
        {
            if (config.GetValue<int?>("Disputes:SellerResponseSeconds") is int seconds)
                return Now.AddSeconds(Math.Max(1, seconds));
            var deadline = Now;
            for (var days = 0; days < 3;)
            {
                deadline = deadline.AddDays(1);
                if (deadline.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days++;
            }
            return deadline;
        }
    }
    private int BuyerSeconds => Math.Max(1, config.GetValue("Disputes:BuyerResponseSeconds", 259200));
    private int ReturnReceiptSeconds => Math.Max(1, config.GetValue("Disputes:ReturnReceiptSeconds", 172800));
    private int ExecutionSeconds => Math.Clamp(config.GetValue("Disputes:ExecutionTimeoutSeconds", 5), 1, 10);

    public async Task<Dispute> OpenAsync(int orderId, int buyerId, OpenDisputeRequest request, CancellationToken ct = default)
    {
        var links = ValidateEvidence(request.Description, request.EvidenceLinks);
        Dispute? result = null;
        await AtomicAsync(async () =>
        {
            var order = await (db.Database.IsSqlServer()
                ? db.OrderTables.FromSqlInterpolated($"SELECT * FROM [OrderTable] WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {orderId}")
                : db.OrderTables.Where(x => x.Id == orderId)).SingleOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("Không tìm thấy đơn hàng.");
            if (order.BuyerId != buyerId) throw new UnauthorizedAccessException("Bạn không có quyền mở yêu cầu cho đơn này.");
            result = await db.Disputes.SingleOrDefaultAsync(x => x.OrderId == orderId && x.WorkflowEnabled && x.IsOpen, ct);
            if (result is not null) return;
            if (await db.ReturnRequests.AnyAsync(r => r.OrderId == orderId && r.Status != "Rejected" && r.Status != "Closed" && r.Status != "Refunded", ct))
                throw new InvalidOperationException("Đơn đang xử lý trả hàng. Theo dõi yêu cầu trả hàng hoặc chờ người bán từ chối trước khi mở yêu cầu giải quyết.");
            var delivered = await db.ShippingInfos.Where(x => x.OrderId == orderId && x.Direction == "Outbound")
                .Select(x => x.DeliveredAt).FirstOrDefaultAsync(ct);
            if (order.Status != "Delivered" || delivered is null || delivered < Now.AddDays(-7) ||
                await db.Refunds.AnyAsync(x => x.OrderId == orderId && x.Status == "Succeeded", ct))
                throw new InvalidOperationException("Đơn chưa đủ điều kiện hoặc đã hết thời hạn mở yêu cầu giải quyết.");
            var payment = await db.Payments.SingleAsync(x => x.OrderId == orderId && x.Status == "Succeeded", ct);
            await finance.RecordSuccessfulPaymentAsync(orderId, payment.Id, ct);
            result = new Dispute
            {
                OrderId = orderId, RaisedBy = buyerId, Description = request.Description.Trim(),
                WorkflowEnabled = true, IsOpen = true, Status = "AwaitingSeller", CreatedAt = Now,
                UpdatedAt = Now, SellerResponseDueAt = SellerDeadline
            };
            db.Disputes.Add(result);
            await db.SaveChangesAsync(ct);
            var holdDescription = $"Tranh chấp #{result.Id}: {result.Description}";
            await finance.PlaceHoldAsync(orderId, holdDescription[..Math.Min(500, holdDescription.Length)], ct, result.Id);
            await AppendAsync(result, buyerId, "buyer", "Evidence", result.Description, links, ct);
        }, ct);
        return result!;
    }

    public async Task AddEvidenceAsync(int id, int actorId, string role, DisputeEvidenceRequest request, CancellationToken ct = default)
    {
        var links = ValidateEvidence(request.Description, request.EvidenceLinks);
        await AtomicAsync(async () =>
        {
            var dispute = await OwnedAsync(id, actorId, role, ct);
            if (!dispute.IsOpen || dispute.Outcome is not null)
                throw new InvalidOperationException("Yêu cầu đã có kết quả hoặc đang thực hiện thỏa thuận; không thể bổ sung bằng chứng.");
            await AppendAsync(dispute, actorId, role, "Evidence", request.Description.Trim(), links, ct);
        }, ct);
    }

    public async Task<Dispute> ReportReturnIssueAsync(int returnId, int sellerId, DisputeEvidenceRequest request,
        CancellationToken ct = default)
    {
        var links = ValidateEvidence(request.Description, request.EvidenceLinks);
        Dispute? result = null;
        await AtomicAsync(async () =>
        {
            var orderId = await db.ReturnRequests.Where(x => x.Id == returnId).Select(x => x.OrderId!.Value).SingleAsync(ct);
            var order = db.Database.IsSqlServer()
                ? await db.OrderTables.FromSqlInterpolated($"SELECT * FROM [OrderTable] WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {orderId}").SingleAsync(ct)
                : await db.OrderTables.SingleAsync(x => x.Id == orderId, ct);
            if (order.SellerId != sellerId) throw new UnauthorizedAccessException("Bạn không có quyền xử lý hàng trả này.");
            var returned = await db.ReturnRequests.SingleAsync(x => x.Id == returnId, ct);
            if (db.Database.IsSqlServer()) await db.Entry(returned).ReloadAsync(ct);
            result = await db.Disputes.SingleOrDefaultAsync(x => x.OrderId == orderId && x.WorkflowEnabled && x.IsOpen, ct);
            if (returned.Status == "Disputed" && result?.Status == "Escalated") return;
            var parcel = await db.ShippingInfos.SingleAsync(x => x.OrderId == orderId && x.Direction == "Return", ct);
            var due = returned.ConfirmationDueAt ?? parcel.DeliveredAt?.AddSeconds(Math.Max(1, config.GetValue("Returns:ReceiptSeconds", 172800)));
            if (returned.Status is not ("Approved" or "ReturnShipping") || parcel.Status != "Delivered" ||
                due is null || Now >= due || await db.Refunds.AnyAsync(x => x.OrderId == orderId && x.Status == "Succeeded", ct))
                throw new InvalidOperationException("Hàng chưa giao tới người bán hoặc đã hết hạn báo vấn đề hàng trả.");
            var payment = await db.Payments.SingleAsync(x => x.OrderId == orderId && x.Status == "Succeeded", ct);
            var settlement = await finance.RecordSuccessfulPaymentAsync(orderId, payment.Id, ct);
            if (settlement.Status != "OnHold") await finance.PlaceHoldAsync(orderId, $"Vấn đề hàng trả #{returnId}", ct);
            result ??= new Dispute { OrderId = orderId, RaisedBy = sellerId, Description = request.Description.Trim(),
                WorkflowEnabled = true, IsOpen = true, CreatedAt = Now, SellerResponseDueAt = Now };
            if (result.Id == 0) db.Disputes.Add(result);
            result.Outcome = null;
            result.Proposal = null;
            result.Resolution = null;
            result.Status = "Escalated";
            result.EscalatedAt = Now;
            returned.Status = "Disputed";
            order.UpdatedAt = Now;
            await db.SaveChangesAsync(ct);
            await AppendAsync(result, sellerId, "seller", "ReturnIssue", request.Description.Trim(), links, ct);
        }, ct);
        return result!;
    }

    public async Task ProposeAsync(int id, int sellerId, DisputeProposalRequest request, CancellationToken ct = default)
    {
        var links = ValidateEvidence(request.Description, request.EvidenceLinks);
        if (request.Proposal is not ("Refund" or "ReturnRefund" or "KeepOrder"))
            throw new ArgumentException("Vui lòng chọn một phương án giải quyết hợp lệ.");
        await AtomicAsync(async () =>
        {
            var dispute = await OwnedAsync(id, sellerId, "seller", ct);
            if (dispute.Status != "AwaitingSeller" || Now >= dispute.SellerResponseDueAt)
                throw new InvalidOperationException("Thời hạn phản hồi đã hết hoặc phương án đã được gửi. Hãy làm mới yêu cầu.");
            dispute.Proposal = request.Proposal;
            dispute.Status = "AwaitingBuyer";
            dispute.SellerRespondedAt = Now;
            dispute.BuyerResponseDueAt = Now.AddSeconds(BuyerSeconds);
            await AppendAsync(dispute, sellerId, "seller", "Proposal", request.Description.Trim(), links, ct);
        }, ct);
    }

    public async Task RespondAsync(int id, int buyerId, DisputeResponseRequest request, CancellationToken ct = default)
    {
        if (!request.Accept) ValidateDescription(request.Description);
        var executeAgreement = false;
        await AtomicAsync(async () =>
        {
            var dispute = await OwnedAsync(id, buyerId, "buyer", ct);
            if (dispute.Status != "AwaitingBuyer" || dispute.BuyerResponseDueAt is null || Now >= dispute.BuyerResponseDueAt)
                throw new InvalidOperationException("Thời hạn phản hồi đã hết hoặc yêu cầu đã thay đổi. Hãy làm mới để xem kết quả.");
            await AppendAsync(dispute, buyerId, "buyer", "Response",
                request.Accept ? "Người mua đã chấp nhận phương án." : request.Description.Trim(), [], ct);
            if (!request.Accept)
            {
                await EscalateAsync(dispute, "Hai bên chưa thống nhất; người mua yêu cầu admin xử lý.", ct);
                return;
            }
            if (dispute.Proposal == "KeepOrder")
            {
                await finance.ResolveHoldAsync(dispute.OrderId!.Value, true, ct, await HoldCaseIdAsync(dispute, ct));
                await CloseAsync(dispute, "AgreedKeepOrder", "Hai bên thống nhất giữ nguyên đơn hàng.", ct);
                return;
            }
            dispute.Status = "ExecutingAgreement";
            dispute.Outcome = dispute.Proposal == "ReturnRefund" ? "AgreedReturn" : "AgreedRefund";
            await db.SaveChangesAsync(ct);
            executeAgreement = true;
        }, ct);
        // Acceptance must survive a timeout or failure in an external shipment/refund service.
        if (executeAgreement) await ExecuteAgreementAsync(id, ct);
    }

    public async Task ResolveAsync(int id, int adminId, DisputeResolutionRequest request, CancellationToken ct = default)
    {
        ValidateDescription(request.Reason);
        if (!await db.Users.AnyAsync(x => x.Id == adminId && x.Role == "admin", ct))
            throw new UnauthorizedAccessException("Chỉ admin được quyết định tranh chấp.");
        var executeAgreement = false;
        await AtomicAsync(async () =>
        {
            var dispute = await GetAsync(id, ct);
            if (dispute.Status != "Escalated")
                throw new InvalidOperationException("Admin chỉ xử lý yêu cầu đã được chuyển lên và chưa có quyết định.");
            dispute.Resolution = request.Reason.Trim();
            await AppendAsync(dispute, adminId, "admin", "Decision", request.Reason.Trim(), [], ct);
            if (!request.BuyerWins)
            {
                await finance.ResolveHoldAsync(dispute.OrderId!.Value, true, ct, await HoldCaseIdAsync(dispute, ct));
                var returned = await db.ReturnRequests.SingleOrDefaultAsync(x => x.OrderId == dispute.OrderId && x.Status == "Disputed", ct);
                if (returned is not null) returned.Status = "Closed";
                await CloseAsync(dispute, "SellerWins", dispute.Resolution, ct);
                return;
            }
            dispute.Status = "ExecutingAgreement";
            dispute.Outcome = "BuyerWins";
            dispute.Proposal = "Refund";
            await db.SaveChangesAsync(ct);
            executeAgreement = true;
        }, ct);
        if (executeAgreement) await ExecuteAgreementAsync(id, ct);
    }

    public async Task<DisputePage> GetPageAsync(int actorId, string role, int page, int pageSize, string filter, CancellationToken ct = default)
    {
        var query = VisibleQuery(actorId, role);
        var openCount = await query.CountAsync(x => x.IsOpen, ct);
        query = filter switch { "open" => query.Where(x => x.IsOpen), "closed" => query.Where(x => !x.IsOpen), _ => query };
        pageSize = Math.Clamp(pageSize, 1, 50);
        var count = await query.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (count + pageSize - 1) / pageSize));
        var items = await Summaries(query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        return new DisputePage(page, pageSize, count, openCount, items);
    }

    public async Task<DisputeDetail> GetDetailAsync(int id, int actorId, string role, CancellationToken ct = default)
    {
        var summary = await Summaries(VisibleQuery(actorId, role).Where(x => x.Id == id)).SingleOrDefaultAsync(ct);
        if (summary is null) throw new UnauthorizedAccessException("Bạn không có quyền xem yêu cầu này.");
        var entries = await db.DisputeEntries.AsNoTracking().Where(x => x.DisputeId == id).OrderBy(x => x.Id).ToListAsync(ct);
        return new DisputeDetail(summary, entries.Select(x => new DisputeEntryView(x.Id, x.ActorId, x.ActorRole,
            x.Kind, x.Description, JsonSerializer.Deserialize<string[]>(x.EvidenceLinksJson) ?? [], x.CreatedAt)).ToList());
    }

    public async Task MaintainAsync(CancellationToken ct = default)
    {
        await ImportLegacyHoldsAsync(ct);
        var now = Now;
        var ids = await db.Disputes.AsNoTracking().Where(x => x.WorkflowEnabled && x.IsOpen &&
                (x.Status == "AwaitingSeller" && x.SellerResponseDueAt <= now ||
                 x.Status == "AwaitingBuyer" && x.BuyerResponseDueAt <= now ||
                 db.Refunds.Any(r => r.OrderId == x.OrderId && r.Status == "Succeeded")))
            .OrderBy(x => x.UpdatedAt)
            .Select(x => x.Id).Take(100).ToListAsync(ct);
        foreach (var id in ids)
        {
            await RunCaseAsync(id, async dispute =>
            {
                if (!dispute.IsOpen) return;
                if (await db.Refunds.AnyAsync(x => x.OrderId == dispute.OrderId && x.Status == "Succeeded", ct))
                    await CloseAsync(dispute, dispute.Outcome ?? "RefundCompleted", dispute.Resolution ?? "Đơn hàng đã được hoàn tiền thành công.", ct);
                else if (dispute.Status == "AwaitingSeller" && Now >= dispute.SellerResponseDueAt)
                    await EscalateAsync(dispute, "Người bán không phản hồi đúng hạn; hệ thống tự chuyển admin xử lý.", ct);
                else if (dispute.Status == "AwaitingBuyer" && Now >= dispute.BuyerResponseDueAt)
                {
                    await finance.ResolveHoldAsync(dispute.OrderId!.Value, true, ct, await HoldCaseIdAsync(dispute, ct));
                    await CloseAsync(dispute, "BuyerSilent", "Đã đóng do người mua không phản hồi phương án đúng hạn.", ct);
                }
            }, ct);
        }
        // Resolve deadlines before external work, then bound the whole retry batch.
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var agreements = await db.Disputes.AsNoTracking().Where(x => x.WorkflowEnabled && x.IsOpen &&
                x.Status == "ExecutingAgreement").OrderBy(x => x.UpdatedAt).Select(x => x.Id).Take(10).ToListAsync(ct);
        foreach (var id in agreements)
        {
            var remaining = TimeSpan.FromSeconds(ExecutionSeconds) - System.Diagnostics.Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) break;
            await ExecuteAgreementAsync(id, ct, remaining);
        }
    }

    private async Task ExecuteAgreementAsync(int id, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(timeout ?? TimeSpan.FromSeconds(ExecutionSeconds));
        await RunCaseAsync(id, async dispute =>
        {
            await CompleteAgreementAsync(dispute, budget.Token);
            dispute.UpdatedAt = Now;
            await db.SaveChangesAsync(budget.Token);
        }, ct, budget.Token);
    }

    private async Task RunCaseAsync(int id, Func<Dispute, Task> work, CancellationToken ct,
        CancellationToken? executionToken = null)
    {
        try
        {
            var workToken = executionToken ?? ct;
            await AtomicAsync(async () => await work(await GetAsync(id, workToken)), workToken);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger?.LogWarning(ex, "Dispute {DisputeId} remains pending; execution will be retried", id);
            // Remove rolled-back tracked changes before continuing with other cases and worker tasks.
            db.ChangeTracker.Clear();
            var pending = await GetAsync(id, ct);
            pending.UpdatedAt = Now;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task CompleteAgreementAsync(Dispute dispute, CancellationToken ct)
    {
        var orderId = dispute.OrderId!.Value;
        if (await db.Refunds.AnyAsync(x => x.OrderId == orderId && x.Status == "Succeeded", ct))
        {
            await CloseAsync(dispute, dispute.Outcome!, dispute.Resolution ?? "Đã hoàn tiền theo thỏa thuận.", ct);
            return;
        }
        Refund? refund = null;
        if (dispute.Proposal == "ReturnRefund")
        {
            var request = await returns.RequestReturnAsync(orderId, dispute.Description!, ct);
            if (request.Status == "Rejected")
            {
                request.Status = "Requested";
                await db.SaveChangesAsync(ct);
            }
            if (request.Status == "Requested") request = await returns.ApproveAsync(request.Id, ct);
            var parcel = await db.ShippingInfos.SingleOrDefaultAsync(x => x.OrderId == orderId && x.Direction == "Return", ct);
            if (request.Status == "Approved" && parcel?.TrackingNumber is null)
                await returns.RetryShipmentAsync(request.Id, ct);
            if (request.Status is ("Approved" or "ReturnShipping") && parcel?.Status == "Delivered" &&
                parcel.DeliveredAt is not null && Now >= parcel.DeliveredAt.Value.AddSeconds(ReturnReceiptSeconds))
                request = await returns.MarkReceivedAsync(request.Id, ct);
            if (request.Status is "ReceivedBySeller" or "RefundFailed" or "RefundPending")
                refund = await returns.RefundAsync(orderId, "return", request.Id, ct);
            if (request.Status == "Refunded")
                refund = await db.Refunds.SingleAsync(x => x.Id == request.RefundId, ct);
        }
        else
        {
            var settlement = await db.SellerSettlements.SingleAsync(x => x.OrderId == orderId, ct);
            if (settlement.Status == "OnHold")
                await finance.ResolveHoldAsync(orderId, false, ct, await HoldCaseIdAsync(dispute, ct));
            refund = await returns.RefundAsync(orderId, "dispute", null, ct);
        }
        if (refund?.Status == "Succeeded")
            await CloseAsync(dispute, dispute.Outcome!, dispute.Resolution ?? "Đã hoàn tiền theo thỏa thuận.", ct);
    }

    private async Task EscalateAsync(Dispute dispute, string reason, CancellationToken ct)
    {
        dispute.Status = "Escalated";
        dispute.EscalatedAt = Now;
        await AppendAsync(dispute, null, "system", "Escalated", reason, [], ct);
    }

    private async Task CloseAsync(Dispute dispute, string outcome, string reason, CancellationToken ct)
    {
        dispute.Status = "Closed";
        dispute.IsOpen = false;
        dispute.Outcome = outcome;
        dispute.Resolution = reason;
        dispute.ClosedAt = Now;
        await AppendAsync(dispute, null, "system", "Closed", reason, [], ct);
    }

    private async Task AppendAsync(Dispute dispute, int? actorId, string role, string kind, string description, string[] links, CancellationToken ct)
    {
        var entry = new DisputeEntry { DisputeId = dispute.Id, ActorId = actorId, ActorRole = role,
            Kind = kind, Description = description, EvidenceLinksJson = JsonSerializer.Serialize(links), CreatedAt = Now };
        db.DisputeEntries.Add(entry);
        dispute.UpdatedAt = Now;
        var order = await db.OrderTables.SingleAsync(x => x.Id == dispute.OrderId, ct);
        order.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        var recipients = await db.Users.Where(x => x.Id == order.BuyerId || x.Id == order.SellerId).ToListAsync(ct);
        foreach (var user in recipients)
        {
            db.NotificationOutbox.Add(new NotificationOutbox
            {
                OrderId = order.Id, EventType = $"Case{dispute.Id}Entry{entry.Id}User{user.Id}",
                Recipient = user.Email ?? "", Subject = $"Cập nhật yêu cầu giải quyết đơn #{order.Id}",
                Body = $"{description}\nVui lòng mở tab Tranh chấp để xem chi tiết và thời hạn phản hồi.", CreatedAt = Now
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task ImportLegacyHoldsAsync(CancellationToken ct)
    {
        var ids = await db.SellerSettlements.AsNoTracking().Where(x => (x.Status == "OnHold" || x.Status == "RefundPending") &&
            !db.Disputes.Any(d => d.OrderId == x.OrderId && d.WorkflowEnabled) &&
            !db.ReturnRequests.Any(r => r.OrderId == x.OrderId && r.Status != "Requested" && r.Status != "Rejected" && r.Status != "Closed" && r.Status != "Refunded"))
            .Select(x => x.OrderId).Take(50).ToListAsync(ct);
        foreach (var orderId in ids)
        {
            await AtomicAsync(async () =>
            {
                if (await db.Disputes.AnyAsync(x => x.OrderId == orderId && x.WorkflowEnabled, ct)) return;
                var order = await db.OrderTables.SingleAsync(x => x.Id == orderId, ct);
                var pending = await db.SellerSettlements.AnyAsync(x => x.OrderId == orderId && x.Status == "RefundPending", ct);
                var dispute = new Dispute { OrderId = orderId, RaisedBy = order.BuyerId, WorkflowEnabled = true, IsOpen = true,
                    Description = "Khoản giữ tiền từ trước khi nâng cấp quy trình tranh chấp.",
                    Status = pending ? "ExecutingAgreement" : "Escalated", Proposal = pending ? "Refund" : null,
                    Outcome = pending ? "BuyerWins" : null, CreatedAt = Now, UpdatedAt = Now,
                    SellerResponseDueAt = Now, EscalatedAt = Now };
                db.Disputes.Add(dispute);
                await db.SaveChangesAsync(ct);
                await AppendAsync(dispute, null, "system", "Imported", dispute.Description, [], ct);
            }, ct);
        }
    }

    private IQueryable<Dispute> VisibleQuery(int actorId, string role)
    {
        var query = db.Disputes.AsNoTracking().Where(x => x.WorkflowEnabled && x.Order != null);
        return role switch
        {
            "buyer" => query.Where(x => x.Order!.BuyerId == actorId),
            "seller" => query.Where(x => x.Order!.SellerId == actorId),
            "admin" => query.Where(x => x.EscalatedAt != null),
            _ => throw new UnauthorizedAccessException("Bạn không có quyền truy cập tranh chấp.")
        };
    }

    private IQueryable<DisputeSummary> Summaries(IQueryable<Dispute> query) => query.Select(x => new DisputeSummary(
        x.Id, x.OrderId!.Value, x.Order!.BuyerId ?? 0, x.Order.SellerId ?? 0, x.Status!, x.IsOpen,
        x.Description!, x.Proposal, x.Outcome, x.Resolution, x.CreatedAt, x.UpdatedAt,
        x.Status == "AwaitingSeller" ? x.SellerResponseDueAt : x.Status == "AwaitingBuyer" ? x.BuyerResponseDueAt : null,
        x.Order.TotalPrice ?? 0m, x.IsOpen && db.SellerSettlements.Any(s => s.OrderId == x.OrderId &&
            (s.Status == "OnHold" || s.Status == "RefundPending"))
            ? db.FinancialTransactions.Where(t => t.OrderId == x.OrderId && t.Type == "FundHold")
                .OrderByDescending(t => t.Id).Select(t => t.Amount).FirstOrDefault() : 0m));

    private async Task<Dispute> OwnedAsync(int id, int actorId, string role, CancellationToken ct)
    {
        var dispute = await GetAsync(id, ct);
        var order = await db.OrderTables.SingleAsync(x => x.Id == dispute.OrderId, ct);
        if (!(role == "buyer" && order.BuyerId == actorId || role == "seller" && order.SellerId == actorId))
            throw new UnauthorizedAccessException("Bạn không có quyền cập nhật yêu cầu này.");
        return dispute;
    }

    private Task<Dispute> GetAsync(int id, CancellationToken ct) => db.Disputes.SingleAsync(x => x.Id == id && x.WorkflowEnabled, ct);

    private async Task<int?> HoldCaseIdAsync(Dispute dispute, CancellationToken ct) =>
        await db.FinancialTransactions.AnyAsync(x => x.EntryKey == $"order:{dispute.OrderId}:case:{dispute.Id}:hold", ct) ? dispute.Id : null;

    private static void ValidateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 4000)
            throw new ArgumentException("Vui lòng nhập mô tả từ 1 đến 4.000 ký tự.");
    }

    private static string[] ValidateEvidence(string? description, string[]? links)
    {
        ValidateDescription(description);
        if (links is null || links.Length is < 1 or > 10) throw new ArgumentException("Vui lòng cung cấp từ 1 đến 10 link bằng chứng.");
        var normalized = links.Select(x => x?.Trim() ?? "").Distinct().ToArray();
        if (normalized.Any(x => x.Length > 2000 || !Uri.TryCreate(x, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new ArgumentException("Link bằng chứng phải là địa chỉ http hoặc https hợp lệ.");
        return normalized;
    }

    private async Task AtomicAsync(Func<Task> work, CancellationToken ct)
    {
        // Serialize transitions and money changes; row versions reject stale updates.
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        await work();
        if (transaction is not null) await transaction.CommitAsync(ct);
    }
}
