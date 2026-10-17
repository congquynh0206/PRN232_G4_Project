using G4.Contracts.Checkout;
using G4.Domain.Entities;

namespace G4.Application.Services;
public interface ISellerFinanceService
{
    Task ValidateNewSaleAsync(int sellerId, CancellationToken ct = default);
    Task<int> MaintainDebtsAsync(CancellationToken ct = default);
    Task<int> MaintainWriteOffsAsync(CancellationToken ct = default);
    Task<int> MaintainLevelsAsync(CancellationToken ct = default);
    Task<SellerLevelDetails> GetLevelDetailsAsync(int sellerId, CancellationToken ct = default);
    Task<SellerDebtSummary> GetDebtAsync(int sellerId, CancellationToken ct = default);
    Task<DebtRepaymentView> RepayDebtAsync(int sellerId, DebtRepaymentRequest request, CancellationToken ct = default);
    Task ValidateMonthlyLimitAsync(int orderId, CancellationToken ct = default);
    Task<SellerSettlement> RecordSuccessfulPaymentAsync(int orderId, int paymentId, CancellationToken ct = default);
    Task ApplyRefundAsync(int orderId, int refundId, CancellationToken ct = default);
    Task ApplyShippingCostAsync(int orderId, int requestId, decimal amount, CancellationToken ct = default);
    Task ApplyReturnShippingCostAsync(int orderId, int returnId, decimal amount, CancellationToken ct = default);
    Task PlaceHoldAsync(
        int orderId,
        string reason,
        CancellationToken ct = default,
        int? disputeId = null
    );
    Task ResolveHoldAsync(
        int orderId,
        bool releaseToSeller,
        CancellationToken ct = default,
        int? disputeId = null
    );
    Task<int> BackfillAsync(CancellationToken ct = default);
    Task<int> ReleaseDueFundsAsync(CancellationToken ct = default);
    Task StartDeliveryHoldAsync(int orderId, CancellationToken ct = default);
    Task<int> AdvancePayoutsAsync(CancellationToken ct = default);
    Task<SellerPayout> RequestPayoutAsync(
        int sellerId,
        decimal amount,
        string key,
        bool simulateFailure,
        CancellationToken ct = default
    );
    Task<SellerFinanceSummary> GetSummaryAsync(int sellerId, CancellationToken ct = default);
    Task<SellerFinanceSummary> EvaluateLevelAsync(int sellerId, CancellationToken ct = default);
}
