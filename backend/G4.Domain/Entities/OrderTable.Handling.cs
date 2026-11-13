namespace G4.Domain.Entities;

public partial class OrderTable
{
    // Handling time snapshotted when the order is created; null for pre-upgrade orders.
    public int? HandlingSeconds { get; set; }

    // Set once when the order first becomes Paid: payment time + HandlingSeconds.
    public DateTime? HandlingDeadlineAt { get; set; }

    // Set once when the seller's outbound label is created successfully.
    public DateTime? HandedOverAt { get; set; }
}
