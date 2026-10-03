namespace G4.Contracts.Checkout;

public sealed record OpenDisputeRequest(string Description, string[] EvidenceLinks);
public sealed record DisputeEvidenceRequest(string Description, string[] EvidenceLinks);
public sealed record DisputeProposalRequest(string Proposal, string Description, string[] EvidenceLinks);
public sealed record DisputeResponseRequest(bool Accept, string Description);
public sealed record DisputeResolutionRequest(bool BuyerWins, string Reason);
public sealed record DisputeSummary(int Id, int OrderId, int BuyerId, int SellerId, string Status,
    bool IsOpen, string Description, string? Proposal, string? Outcome, string? Resolution,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? ResponseDueAt, decimal TotalPrice, decimal HeldAmount);
public sealed record DisputeEntryView(int Id, int? ActorId, string ActorRole, string Kind,
    string Description, string[] EvidenceLinks, DateTime CreatedAt);
public sealed record DisputeDetail(DisputeSummary Case, IReadOnlyList<DisputeEntryView> Entries);
public sealed record DisputePage(int Page, int PageSize, int TotalCount, int OpenCount, IReadOnlyList<DisputeSummary> Items);
