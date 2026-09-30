using G4.Domain.Entities;

namespace G4.Application.Services;

public interface IReturnService
{
    Task<ReturnRequest> RequestReturnAsync(int orderId, string reason, CancellationToken ct = default);
    Task<ReturnRequest> ApproveAsync(int returnId, CancellationToken ct = default);
    Task<ReturnRequest> RetryShipmentAsync(int returnId, CancellationToken ct = default);
    Task<ReturnRequest> RejectAsync(int returnId, string reason, CancellationToken ct = default);
    Task<ReturnRequest> MarkReceivedAsync(int returnId, CancellationToken ct = default);
    Task<OrderTable> RequestCancelAsync(int orderId, CancellationToken ct = default);
    Task<OrderTable> DecideCancelAsync(int orderId, bool approve, string? reason = null, CancellationToken ct = default);
    Task<Refund> RefundAsync(int orderId, string reason, int? returnRequestId, CancellationToken ct = default);
}
