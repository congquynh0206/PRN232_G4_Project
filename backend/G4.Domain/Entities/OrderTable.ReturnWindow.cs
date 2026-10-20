namespace G4.Domain.Entities;

public partial class OrderTable
{
    public int ReturnWindowPolicyVersion { get; set; }
    public int? ReturnWindowSeconds { get; set; }
    public DateTime? ReturnWindowStartedAt { get; set; }
    public DateTime? ReturnWindowClosesAt { get; set; }
}
