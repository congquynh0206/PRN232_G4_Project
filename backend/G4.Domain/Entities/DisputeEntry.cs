namespace G4.Domain.Entities;

public sealed class DisputeEntry
{
    public int Id { get; set; }
    public int DisputeId { get; set; }
    public int? ActorId { get; set; }
    public string ActorRole { get; set; } = "system";
    public string Kind { get; set; } = "Evidence";
    public string Description { get; set; } = "";
    public string EvidenceLinksJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
}
