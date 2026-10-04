using G4.Contracts.Checkout;

namespace G4.Application.Services;

public interface IDisputeService
{
    Task<Dispute> OpenAsync(int orderId, int buyerId, OpenDisputeRequest request, CancellationToken ct = default);
    Task<Dispute> ReportReturnIssueAsync(int returnId, int sellerId, DisputeEvidenceRequest request, CancellationToken ct = default);
    Task AddEvidenceAsync(int id, int actorId, string role, DisputeEvidenceRequest request, CancellationToken ct = default);
    Task ProposeAsync(int id, int sellerId, DisputeProposalRequest request, CancellationToken ct = default);
    Task RespondAsync(int id, int buyerId, DisputeResponseRequest request, CancellationToken ct = default);
    Task ResolveAsync(int id, int adminId, DisputeResolutionRequest request, CancellationToken ct = default);
    Task<DisputePage> GetPageAsync(int actorId, string role, int page, int pageSize, string filter, CancellationToken ct = default);
    Task<DisputeDetail> GetDetailAsync(int id, int actorId, string role, CancellationToken ct = default);
    Task MaintainAsync(CancellationToken ct = default);
}
