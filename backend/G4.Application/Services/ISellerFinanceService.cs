using G4.Contracts.Checkout;
using G4.Domain.Entities;

namespace G4.Application.Services;

public interface ISellerFinanceService
{
    Task ValidateMonthlyLimitAsync(int orderId, CancellationToken ct = default);
    Task<SellerSettlement> RecordSuccessfulPaymentAsync(int orderId, int paymentId, CancellationToken ct = default);
    Task ApplyRefundAsync(int orderId, int refundId, CancellationToken ct = default);
    Task PlaceHoldAsync(int orderId, string reason, CancellationToken ct = default);
    Task ResolveHoldAsync(int orderId, bool releaseToSeller, CancellationToken ct = default);
    Task<int> BackfillAsync(CancellationToken ct = default);
    Task<int> ReleaseDueFundsAsync(CancellationToken ct = default);
    Task<int> AdvancePayoutsAsync(CancellationToken ct = default);
    Task<SellerPayout> RequestPayoutAsync(int sellerId, decimal amount, string key, bool simulateFailure,
        CancellationToken ct = default);
    Task<SellerFinanceSummary> GetSummaryAsync(int sellerId, CancellationToken ct = default);
    Task<SellerFinanceSummary> EvaluateLevelAsync(int sellerId, CancellationToken ct = default);
}
