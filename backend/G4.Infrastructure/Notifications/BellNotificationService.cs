using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Notifications;

public sealed class BellNotificationService(ApplicationDbContext db)
{
    // Called only for committed CommerceChange rows. A unique recipient/event index makes
    // retries after a failed SignalR send safe without duplicating the durable inbox.
    public async Task<IReadOnlyList<int>> MaterializeAsync(
        CommerceChange change,
        IReadOnlySet<string> audiences,
        CancellationToken ct
    )
    {
        if (change.NotificationTitle == null || change.NotificationMessage == null)
            return [];
        if (change.OrderId == null && change.ShippingInfoId != null)
            change.OrderId = await db
                .ShippingInfos.Where(x => x.Id == change.ShippingInfoId)
                .Select(x => x.OrderId)
                .SingleOrDefaultAsync(ct);
        var ids = audiences
            .Where(x => x.StartsWith("user:", StringComparison.Ordinal))
            .Select(x => int.Parse(x.AsSpan(5)))
            .ToArray();
        var admins = audiences.Contains("role:admin");
        // Other shippers receive only a generic new-job alert, never updates to assigned jobs.
        var availableShipment =
            audiences.Contains("role:shipper")
            && change.NotificationTitle == "Vận đơn mới"
            && change.ShipperId == null;
        var assignedShippers = new HashSet<int>();
        if (change.Topic == "shipments" || change.NotificationTitle == "Cập nhật đổi địa chỉ")
        {
            if (change.ShipperId > 0)
                assignedShippers.Add(change.ShipperId.Value);
            else if (change.ShippingInfoId != null)
            {
                var assigned = await db
                    .ShippingInfos.Where(x => x.Id == change.ShippingInfoId)
                    .Select(x => x.ShipperId)
                    .SingleOrDefaultAsync(ct);
                if (assigned > 0)
                    assignedShippers.Add(assigned.Value);
            }
        }
        var recipients = await db
            .Users.AsNoTracking()
            .Where(x =>
                ids.Contains(x.Id)
                || (admins && x.Role == "admin")
                || (availableShipment && x.Role == "shipper")
            )
            .Where(x =>
                x.Role == "buyer" || x.Role == "seller" || x.Role == "shipper" || x.Role == "admin"
            )
            .Where(x => x.Role != "shipper" || availableShipment || assignedShippers.Contains(x.Id))
            .Select(x => new { x.Id, x.Role })
            .ToListAsync(ct);
        var existing = await db
            .UserNotifications.Where(x => x.CommerceChangeId == change.Id)
            .Select(x => x.UserId)
            .ToListAsync(ct);
        foreach (var recipient in recipients)
        {
            if (
                existing.Contains(recipient.Id)
                || db.UserNotifications.Local.Any(x =>
                    x.CommerceChangeId == change.Id && x.UserId == recipient.Id
                )
            )
                continue;
            var generalJob =
                recipient.Role == "shipper" && !assignedShippers.Contains(recipient.Id);
            db.UserNotifications.Add(
                new UserNotification
                {
                    CommerceChangeId = change.Id,
                    UserId = recipient.Id,
                    Title = generalJob ? "Có vận đơn chờ nhận" : change.NotificationTitle,
                    Message = generalJob
                        ? "Danh sách vận đơn chờ nhận vừa được cập nhật."
                        : change.NotificationMessage,
                    TargetUrl = Target(change, recipient.Role!, generalJob),
                    CreatedAt = change.CreatedAt,
                }
            );
        }
        return recipients.Select(x => x.Id).ToArray();
    }

    private static string Target(CommerceChange change, string role, bool generalJob)
    {
        if (role == "shipper")
            return !generalJob && change.OrderId > 0 && change.ShippingInfoId > 0
                ? $"/Shipper?orderId={change.OrderId}&shipmentId={change.ShippingInfoId}"
                : "/Shipper";
        if (role == "admin")
            return change.Topic switch
            {
                "disputes" => "/Admin#disputes",
                "finance" => "/Admin#seller-debts",
                "levels" => "/Admin#seller-levels",
                _ when change.OrderId > 0 => $"/Admin?orderId={change.OrderId}#order-lookup",
                _ => "/Admin#disputes",
            };
        var root = role == "buyer" ? "/Buyer" : "/Seller";
        if (change.OrderId > 0)
            return $"{root}?orderId={change.OrderId}#orders";
        return root
            + (
                change.Topic switch
                {
                    "disputes" => "#disputes",
                    "finance" => "#finance",
                    "levels" => role == "seller" ? "#seller-levels" : "#orders",
                    "product-feedback" => "#product-feedback",
                    _ => "#orders",
                }
            );
    }

    public async Task<BellNotificationPage> ListAsync(
        int userId,
        int page,
        int pageSize,
        CancellationToken ct
    )
    {
        pageSize = Math.Clamp(pageSize, 1, 30);
        var query = db.UserNotifications.AsNoTracking().Where(x => x.UserId == userId);
        var total = await query.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (total + pageSize - 1) / pageSize));
        var unread = await query.CountAsync(x => x.ReadAt == null, ct);
        var latest = await query.Select(x => (long?)x.Id).MaxAsync(ct) ?? 0;
        var items = await query
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new BellNotificationView(
                x.Id,
                x.Title,
                x.Message,
                x.TargetUrl,
                x.CreatedAt,
                x.ReadAt
            ))
            .ToListAsync(ct);
        return new(page, pageSize, total, unread, latest, items);
    }

    public async Task<bool> MarkReadAsync(int userId, long id, CancellationToken ct)
    {
        if (!await db.UserNotifications.AnyAsync(x => x.Id == id && x.UserId == userId, ct))
            return false;
        await MarkAsync(userId, id, false, ct);
        return true;
    }

    public Task MarkAllReadAsync(int userId, long throughId, CancellationToken ct) =>
        MarkAsync(userId, throughId, true, ct);

    private async Task MarkAsync(int userId, long id, bool all, CancellationToken ct)
    {
        var query = db.UserNotifications.Where(x =>
            x.UserId == userId && x.ReadAt == null && (all ? x.Id <= id : x.Id == id)
        );
        var now = DateTime.UtcNow;
        if (!db.Database.IsRelational())
        {
            foreach (var item in await query.ToListAsync(ct))
                item.ReadAt = now;
            await db.SaveChangesAsync(ct);
            return;
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var changed = await query.ExecuteUpdateAsync(
            set => set.SetProperty(x => x.ReadAt, now),
            ct
        );
        if (changed > 0)
        {
            db.CommerceChanges.Add(
                new CommerceChange
                {
                    Topic = "notification-inbox",
                    BuyerId = userId,
                    CreatedAt = now,
                }
            );
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }
}
