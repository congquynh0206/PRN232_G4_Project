using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Payments;

public sealed class SimulatedImmediateCardGateway(
    IDbContextFactory<ApplicationDbContext> contexts
) : IImmediateCardGateway
{
    public async Task<CardAuthorizationState> GetAsync(string key, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var authorization = await db.SimulatedCardAuthorizations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == key, ct);
        return authorization == null ? new("NotFound", 0) : View(authorization);
    }

    public Task<CardAuthorizationState> AuthorizeAsync(
        string key, decimal amount, string validatedCardOutcome, CancellationToken ct
    ) => ChangeAsync(key, authorization =>
    {
        if (authorization.Amount != amount)
            throw new InvalidOperationException("Authorization amount mismatch");
        if (authorization.Status == "New")
            authorization.Status = validatedCardOutcome == "Succeeded" ? "Authorized" : "Declined";
    }, amount, ct);

    public Task<CardAuthorizationState> CaptureAsync(string key, CancellationToken ct) =>
        ChangeAsync(key, authorization =>
        {
            if (authorization.Status == "Authorized")
            {
                authorization.Status = "Captured";
                authorization.CaptureId = "CARD-" + key;
            }
        }, null, ct);

    public Task<CardAuthorizationState> VoidAsync(string key, CancellationToken ct) =>
        ChangeAsync(key, authorization =>
        {
            if (authorization.Status == "Authorized")
                authorization.Status = "Voided";
        }, null, ct);

    public Task<CardAuthorizationState> RefundAsync(
        string key, CancellationToken ct, decimal? amount = null, string? refundKey = null
    ) =>
        ChangeAsync(key, authorization =>
        {
            var entries = JsonSerializer.Deserialize<Dictionary<string, decimal>>(
                authorization.RefundsJson)!;
            var requestKey = refundKey ?? "compensation:" + key;
            if (entries.TryGetValue(requestKey, out var existing))
            {
                if (amount != null && existing != amount)
                    throw new InvalidOperationException("Refund key amount mismatch");
                return;
            }
            if (authorization.Status is not ("Captured" or "PartiallyRefunded"))
                throw new InvalidOperationException("Authorization is not refundable for this key");
            var value = amount ?? authorization.Amount - authorization.RefundedAmount;
            if (value < 0 || value > authorization.Amount - authorization.RefundedAmount)
                throw new InvalidOperationException("Refund exceeds remaining captured amount");
            entries[requestKey] = value;
            authorization.RefundedAmount += value;
            authorization.RefundsJson = JsonSerializer.Serialize(entries);
            authorization.Status = authorization.RefundedAmount == authorization.Amount
                ? "Refunded" : "PartiallyRefunded";
        }, null, ct);

    private async Task<CardAuthorizationState> ChangeAsync(
        string key,
        Action<SimulatedCardAuthorization> change,
        decimal? initialAmount,
        CancellationToken ct
    )
    {
        // This context is independent of the order transaction. No PAN/CVV is persisted.
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        if (db.Database.IsSqlServer())
        {
            var resource = "simulated-card:" + key;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @r<0 THROW 51000,'Card operation busy',1;",
                ct);
        }
        var authorization = await db.SimulatedCardAuthorizations
            .SingleOrDefaultAsync(x => x.Id == key, ct);
        if (authorization == null)
        {
            if (initialAmount == null)
                throw new InvalidOperationException("Authorization not found");
            authorization = new() { Id = key, Amount = initialAmount.Value, Status = "New" };
            db.SimulatedCardAuthorizations.Add(authorization);
        }
        change(authorization);
        authorization.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return View(authorization);
    }

    private static CardAuthorizationState View(SimulatedCardAuthorization authorization) =>
        new(authorization.Status, authorization.Amount, authorization.Id, authorization.CaptureId,
            authorization.RefundedAmount);
}
