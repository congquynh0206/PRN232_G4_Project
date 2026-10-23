using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Realtime;

public sealed class CommerceChangeWorker(
    IServiceScopeFactory scopes,
    IHubContext<CommerceHub> hub,
    ILogger<CommerceChangeWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                // A separate context sees only committed business transactions.
                var changes = await db
                    .CommerceChanges.Where(x => x.DispatchedAt == null)
                    .OrderBy(x => x.Id)
                    .Take(100)
                    .ToListAsync(stoppingToken);
                var deliveries = new Dictionary<string, HashSet<string>>();
                foreach (var change in changes)
                {
                    var groups = await AudiencesAsync(db, change, stoppingToken);
                    foreach (var group in groups)
                    {
                        if (!deliveries.TryGetValue(group, out var topics))
                            deliveries[group] = topics = new();
                        topics.Add(change.Topic);
                    }
                }
                foreach (var (group, topics) in deliveries)
                    await hub
                        .Clients.Group(group)
                        .SendAsync(
                            "CommerceChanged",
                            new { version = changes.Max(x => x.Id), topics },
                            stoppingToken
                        );
                foreach (var change in changes)
                    change.DispatchedAt = DateTime.UtcNow;
                if (changes.Count > 0)
                    await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Realtime dispatch will retry");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public static async Task<HashSet<string>> AudiencesAsync(
        ApplicationDbContext db,
        CommerceChange c,
        CancellationToken ct
    )
    {
        var groups = new HashSet<string>();
        void User(int? id)
        {
            if (id > 0)
                groups.Add("user:" + id);
        }
        User(c.BuyerId);
        User(c.SellerId);
        User(c.ShipperId);
        int? orderId = c.OrderId;
        if (c.DisputeId != null)
            orderId ??= await db
                .Disputes.Where(x => x.Id == c.DisputeId)
                .Select(x => x.OrderId)
                .FirstOrDefaultAsync(ct);
        if (c.ShippingInfoId != null)
        {
            var ship = await db
                .ShippingInfos.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == c.ShippingInfoId, ct);
            orderId ??= ship?.OrderId;
            User(ship?.ShipperId);
        }
        if (c.SellerAccountId != null)
            User(
                await db
                    .SellerAccounts.Where(x => x.Id == c.SellerAccountId)
                    .Select(x => (int?)x.SellerId)
                    .FirstOrDefaultAsync(ct)
            );
        if (orderId != null)
        {
            var order = await db
                .OrderTables.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == orderId, ct);
            User(order?.BuyerId);
            User(order?.SellerId);
            foreach (
                var shipper in await db
                    .ShippingInfos.Where(x => x.OrderId == orderId && x.ShipperId != null)
                    .Select(x => x.ShipperId)
                    .Distinct()
                    .ToListAsync(ct)
            )
                User(shipper);
            if (
                await db.Disputes.AnyAsync(
                    x => x.OrderId == orderId && x.WorkflowEnabled && x.EscalatedAt != null,
                    ct
                )
            )
                groups.Add("role:admin");
        }
        if (c.Topic is "finance" or "levels" or "notifications")
            groups.Add("role:admin");
        if (c.Topic == "shipments")
            groups.Add("role:shipper"); // Invalidation only, no order identifiers or contents.
        return groups;
    }
}
