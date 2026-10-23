using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Disputes;

public sealed partial class DisputeService
{
    public async Task<AdminDecisionQuote> PreviewDecisionAsync(
        int id,
        int adminId,
        AdminDecisionPreviewInput input,
        CancellationToken ct = default
    )
    {
        await EnsureAdminAsync(adminId, ct);
        return await BuildDecisionQuoteAsync(id, input, ct);
    }

    public async Task<AdminDecisionView> ResolveDecisionAsync(
        int id,
        int adminId,
        AdminDecisionInput input,
        CancellationToken ct = default
    )
    {
        await EnsureAdminAsync(adminId, ct);
        ValidateDescription(input.Reason);
        if (
            string.IsNullOrWhiteSpace(input.Key)
            || input.Key.Length > 100
            || input.PricingFingerprint?.Length != 64
            || input.EvidenceEntryIds == null
            || input.EvidenceEntryIds.Length is < 1 or > 20
            || input.EvidenceEntryIds.Any(x => x <= 0)
        )
            throw new ArgumentException(
                "Cần khóa quyết định, báo giá và từ 1 đến 20 trao đổi/bằng chứng làm căn cứ."
            );
        var evidence = input.EvidenceEntryIds.Distinct().Order().ToArray();
        var evidenceJson = JsonSerializer.Serialize(evidence);
        var execute = false;
        await AtomicCaseAsync(
            id,
            async () =>
            {
                var dispute = await GetAsync(id, ct);
                // Acquire before decision and finance reads: two orders of one seller
                // must not retain shared range locks while waiting on the account gate.
                if (db.Database.IsSqlServer())
                {
                    var sellerId = await db
                        .OrderTables.Where(x => x.Id == dispute.OrderId)
                        .Select(x => x.SellerId)
                        .SingleAsync(ct);
                    var resource = $"commerce:seller-finance:{sellerId}";
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $@"DECLARE @result int;
                    EXEC @result=sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
                    IF @result<0 THROW 51000, 'Cannot acquire seller finance transaction lock', 1;",
                        ct
                    );
                }
                var existing = await (
                    db.Database.IsSqlServer()
                        ? db.DisputeDecisions.FromSqlInterpolated(
                            $"SELECT * FROM [DisputeDecision] WITH (UPDLOCK, HOLDLOCK) WHERE [DisputeId]={id} OR [RequestKey]={input.Key}"
                        )
                        : db.DisputeDecisions.Where(x =>
                            x.DisputeId == id || x.RequestKey == input.Key
                        )
                ).SingleOrDefaultAsync(ct);
                if (existing != null)
                {
                    if (
                        existing.DisputeId != id
                        || existing.RequestKey != input.Key
                        || existing.AdminId != adminId
                        || existing.Outcome != input.Outcome
                        || existing.Responsibility != input.Responsibility
                        || existing.RequestedAmount != input.RefundAmount
                        || existing.Reason != input.Reason.Trim()
                        || existing.EvidenceEntryIdsJson != evidenceJson
                        || existing.PricingFingerprint != input.PricingFingerprint
                    )
                        throw new InvalidOperationException(
                            "Hồ sơ hoặc khóa đã có quyết định khác. Tải lại để xem kết quả."
                        );
                    return;
                }
                if (
                    await db.DisputeEntries.CountAsync(
                        x =>
                            x.DisputeId == id
                            && evidence.Contains(x.Id)
                            && x.Kind != "Imported"
                            && x.Kind != "Decision"
                            && x.Kind != "Closed",
                        ct
                    ) != evidence.Length
                )
                    throw new ArgumentException(
                        "Căn cứ phải thuộc trao đổi hoặc bằng chứng của chính hồ sơ này."
                    );
                var quote = await BuildDecisionQuoteAsync(
                    id,
                    new(input.Outcome, input.Responsibility, input.RefundAmount),
                    ct
                );
                if (quote.PricingFingerprint != input.PricingFingerprint)
                    throw new InvalidOperationException(
                        "Hồ sơ hoặc khoản tiền đã thay đổi. Xem lại dự kiến trước khi quyết định."
                    );
                var decision = new DisputeDecision
                {
                    DisputeId = id,
                    OrderId = quote.OrderId,
                    AdminId = adminId,
                    CreatedAt = Now,
                    Outcome = input.Outcome,
                    Responsibility = input.Responsibility,
                    Reason = input.Reason.Trim(),
                    EvidenceEntryIdsJson = evidenceJson,
                    RequestKey = input.Key,
                    PricingFingerprint = quote.PricingFingerprint,
                    RequestedAmount = input.RefundAmount,
                    MaximumRefund = quote.MaximumRefund,
                    BuyerRefund = quote.BuyerRefund,
                    OutboundShippingRetained = quote.OutboundShippingRetained,
                    ReturnShippingCost = quote.ReturnShippingCost,
                    BuyerShippingRecovery = quote.BuyerShippingRecovery,
                    SubsidyReversal = quote.SubsidyReversal,
                    SellerGrossRefund = quote.SellerGrossRefund,
                    FeeCredit = quote.FeeCredit,
                    SellerNetDebit = quote.SellerNetDebit,
                    ShippingPayer = quote.ShippingPayer,
                    Currency = quote.Currency,
                };
                db.DisputeDecisions.Add(decision);
                dispute.Resolution = decision.Reason;
                await db.SaveChangesAsync(ct);
                await AppendAsync(
                    dispute,
                    adminId,
                    "admin",
                    "Decision",
                    $"{decision.Reason}\nKết luận trách nhiệm: {(decision.Responsibility == "Seller" ? "người bán" : "người mua")}.\nKhoản buyer nhận theo quyết định: {decision.BuyerRefund:0.00} {decision.Currency}. Căn cứ: {string.Join(", ", evidence.Select(x => $"#{x}"))}.",
                    [],
                    ct
                );
                if (input.Outcome == "SellerWins")
                {
                    await finance.ResolveHoldAsync(
                        quote.OrderId,
                        true,
                        ct,
                        await HoldCaseIdAsync(dispute, ct)
                    );
                    var returned = await db.ReturnRequests.SingleOrDefaultAsync(
                        x => x.OrderId == quote.OrderId && x.Status == "Disputed",
                        ct
                    );
                    if (returned != null)
                        returned.Status = "Closed";
                    await CloseAsync(dispute, "SellerWins", decision.Reason, ct);
                }
                else
                {
                    dispute.Status = "ExecutingAgreement";
                    dispute.Outcome =
                        input.Outcome == "PartialRefund" ? "AdminPartialRefund" : "AdminFullRefund";
                    dispute.Proposal = "Refund";
                    await db.SaveChangesAsync(ct);
                    execute = true;
                }
            },
            ct
        );
        // The immutable decision commits before contacting external providers.
        if (execute)
            await ExecuteAgreementAsync(id, ct);
        return (await DecisionViewAsync(id, ct))!;
    }

    private async Task<AdminDecisionQuote> BuildDecisionQuoteAsync(
        int id,
        AdminDecisionPreviewInput input,
        CancellationToken ct
    )
    {
        var dispute = await db
            .Disputes.AsNoTracking()
            .SingleAsync(x => x.Id == id && x.WorkflowEnabled, ct);
        if (
            dispute.Status != "Escalated"
            || !dispute.IsOpen
            || dispute.EscalatedAt == null
            || await db.DisputeDecisions.AnyAsync(x => x.DisputeId == id, ct)
        )
            throw new InvalidOperationException(
                "Chỉ được quyết định hồ sơ đã chuyển admin và chưa có quyết định."
            );
        var order = await db
            .OrderTables.AsNoTracking()
            .SingleAsync(x => x.Id == dispute.OrderId, ct);
        var payment = await db
            .Payments.AsNoTracking()
            .SingleAsync(x => x.OrderId == order.Id && x.Status == "Succeeded", ct);
        var settlement = await db
            .SellerSettlements.AsNoTracking()
            .SingleAsync(x => x.OrderId == order.Id, ct);
        if (
            payment.Amount == null
            || payment.Amount < 0
            || payment.Amount != order.TotalPrice
            || settlement.Status != "OnHold"
        )
            throw new InvalidOperationException(
                "Payment hoặc tiền giữ không đủ điều kiện phán quyết."
            );
        if (
            await db.Refunds.AnyAsync(
                x => x.OrderId == order.Id && x.Reason != "shipping-adjustment",
                ct
            )
            || await db.Refunds.AnyAsync(
                x =>
                    x.OrderId == order.Id
                    && x.Reason == "shipping-adjustment"
                    && x.Status != "Succeeded",
                ct
            )
        )
            throw new InvalidOperationException(
                "Đơn đã có refund hoặc điều chỉnh ship chưa hoàn tất; cần đối soát trước."
            );
        var adjusted = await db
            .Refunds.Where(x => x.OrderId == order.Id && x.Status == "Succeeded")
            .SumAsync(x => x.Amount, ct);
        var returned = await db
            .ReturnRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrderId == order.Id, ct);
        var parcel = await db
            .ShippingInfos.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrderId == order.Id && x.Direction == "Return", ct);
        if (
            input.Outcome != "SellerWins"
            && parcel?.TrackingNumber != null
            && parcel.Status != "Delivered"
        )
            throw new InvalidOperationException(
                "Đã có vận đơn trả; phải nhận hàng về trước khi quyết định hoàn tiền."
            );
        var subsidy = order.PricingSchemaVersion == 1 ? order.PlatformSubsidy : 0m;
        var reversed = -await db
            .FinancialTransactions.Where(x =>
                x.OrderId == order.Id && x.Type == "PlatformSubsidyReversal"
            )
            .SumAsync(x => x.Amount, ct);
        var postage = settlement.ReturnShippingCost - settlement.ReturnShippingRecovery;
        var price = DisputeDecisionPricing.Calculate(
            input.Outcome,
            input.Responsibility,
            input.RefundAmount,
            payment.Amount.Value - adjusted,
            payment.Amount.Value - order.ShippingFee,
            Math.Max(0, subsidy - reversed),
            postage,
            settlement.GrossAmount,
            settlement.RefundedAmount,
            settlement.PlatformFeeAmount,
            settlement.FeeCreditAmount
        );
        var evidence = await db
            .DisputeEntries.AsNoTracking()
            .Where(x => x.DisputeId == id)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToArrayAsync(ct);
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(
                        new
                        {
                            version = 1,
                            id,
                            orderId = order.Id,
                            paymentId = payment.Id,
                            input,
                            price,
                            postage,
                            order.Currency,
                            adjusted,
                            settlement.RefundedAmount,
                            settlement.FeeCreditAmount,
                            returnStatus = returned?.Status,
                            parcelStatus = parcel?.Status,
                            disputeStatus = dispute.Status,
                            dispute.UpdatedAt,
                            evidence,
                        }
                    )
                )
            )
        );
        return new(
            id,
            order.Id,
            input.Outcome,
            input.Responsibility,
            price.MaximumRefund,
            price.BuyerRefund,
            price.OutboundShippingRetained,
            postage,
            price.BuyerShippingRecovery,
            price.SubsidyReversal,
            price.SellerGrossRefund,
            price.FeeCredit,
            price.SellerNetDebit,
            price.ShippingPayer,
            order.Currency,
            fingerprint
        );
    }

    private async Task EnsureAdminAsync(int adminId, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == adminId && x.Role == "admin", ct))
            throw new UnauthorizedAccessException("Chỉ admin được quyết định tranh chấp.");
    }

    private async Task<AdminDecisionView?> DecisionViewAsync(int disputeId, CancellationToken ct)
    {
        var d = await db
            .DisputeDecisions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.DisputeId == disputeId, ct);
        if (d == null)
            return null;
        var adminName = await db
            .Users.Where(x => x.Id == d.AdminId)
            .Select(x => x.Username ?? x.Email ?? "Admin")
            .SingleAsync(ct);
        var refund = await db
            .Refunds.AsNoTracking()
            .SingleOrDefaultAsync(x => x.DisputeDecisionId == d.Id, ct);
        return new(
            d.Id,
            d.AdminId,
            adminName,
            d.Outcome,
            d.Responsibility,
            d.Reason,
            JsonSerializer.Deserialize<int[]>(d.EvidenceEntryIdsJson)!,
            d.CreatedAt,
            d.MaximumRefund,
            d.BuyerRefund,
            d.OutboundShippingRetained,
            d.ReturnShippingCost,
            d.BuyerShippingRecovery,
            d.SubsidyReversal,
            d.SellerGrossRefund,
            d.FeeCredit,
            d.SellerNetDebit,
            d.ShippingPayer,
            d.Currency,
            refund?.Status,
            refund?.Status == "Succeeded" ? refund.Amount : null
        );
    }

    private async Task AtomicCaseAsync(int id, Func<Task> work, CancellationToken ct)
    {
        // Resolve the immutable order ID before opening our transaction, avoiding a
        // shared dispute lock followed by a competing order update lock.
        var orderId = await db
            .Disputes.AsNoTracking()
            .Where(x => x.Id == id && x.WorkflowEnabled)
            .Select(x => x.OrderId!.Value)
            .SingleAsync(ct);
        await AtomicAsync(
            async () =>
            {
                if (db.Database.IsSqlServer())
                    await db
                        .OrderTables.FromSqlInterpolated(
                            $"SELECT * FROM [OrderTable] WITH (UPDLOCK, HOLDLOCK) WHERE [id]={orderId}"
                        )
                        .SingleAsync(ct);
                await work();
            },
            ct
        );
    }
}
