using G4.Infrastructure.Shipping;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;

public partial class ApplicationDbContext
{
    // Every payment path (card, PayPal, PayPal cart, Klarna, Buy It Now, Best Offer, zero-total
    // promotion) ends by saving an order whose status became Paid. Starting the handling clock
    // here keeps one rule for all of them instead of repeating it at each call site.
    private void StartHandlingClocks()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<OrderTable>())
        {
            var order = entry.Entity;
            if (order.Status != "Paid" || order.HandlingDeadlineAt != null)
                continue;
            var becamePaid =
                entry.State == EntityState.Added
                || entry.State == EntityState.Modified
                    && entry.Property(x => x.Status).OriginalValue != "Paid";
            if (becamePaid)
                ShippingHandlingPolicy.StartClock(order, now);
        }
    }
}
