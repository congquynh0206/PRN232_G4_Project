using G4.Contracts.Checkout;
using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Finance;

public static class PayoutQuery
{
    public static async Task<List<PayoutView>> ReadAsync(ApplicationDbContext db, IQueryable<SellerPayout> ownedQuery, CancellationToken ct)
    {
        var rows = await ownedQuery.Select(p => new PayoutView(p.Id, p.Amount, p.Currency, p.Status, p.DestinationMasked,
            p.BankReferenceId, p.CreatedAt, p.CompletedAt,
            db.SellerPayoutEvents.Where(e => e.PayoutId == p.Id).OrderBy(e => e.Id)
                .Select(e => new PayoutStepView(e.FromStatus, e.ToStatus, e.OccurredAt, e.ActorUserId, e.ActorRole, e.BankReferenceId)).ToList(),
            p.WorkflowVersion)).ToListAsync(ct);
        return rows.Select(p => p with { CreatedAt = Utc(p.CreatedAt), CompletedAt = p.CompletedAt == null ? null : Utc(p.CompletedAt.Value),
            History = p.History!.Select(e => e with { OccurredAt = Utc(e.OccurredAt) }).ToArray() }).ToList();
    }
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
