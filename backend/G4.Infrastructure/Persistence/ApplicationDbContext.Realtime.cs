using G4.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<CommerceChange> CommerceChanges => Set<CommerceChange>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();

    private static void ConfigureRealtime(ModelBuilder model)
    {
        model.Entity<CommerceChange>(e =>
        {
            e.ToTable("CommerceChange");
            e.Property(x => x.Topic).HasMaxLength(30);
            e.Property(x => x.NotificationTitle).HasMaxLength(160);
            e.Property(x => x.NotificationMessage).HasMaxLength(500);
            e.HasIndex(x => new { x.DispatchedAt, x.Id });
        });
        model.Entity<UserNotification>(e =>
        {
            e.ToTable("UserNotification");
            e.Property(x => x.Title).HasMaxLength(160);
            e.Property(x => x.Message).HasMaxLength(500);
            e.Property(x => x.TargetUrl).HasMaxLength(200);
            e.HasIndex(x => new { x.UserId, x.CommerceChangeId }).IsUnique();
            e.HasIndex(x => new
            {
                x.UserId,
                x.ReadAt,
                x.Id,
            });
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CommerceChange>()
                .WithMany()
                .HasForeignKey(x => x.CommerceChangeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private void CaptureCommerceChanges()
    {
        ChangeTracker.DetectChanges();
        var signals = new List<CommerceChange>();
        foreach (
            var entry in ChangeTracker
                .Entries()
                .Where(x =>
                    x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                )
                .ToArray()
        )
        {
            var signal = entry.Entity switch
            {
                OrderTable x => new CommerceChange
                {
                    Topic = "orders",
                    OrderId = x.Id > 0 ? x.Id : null,
                    BuyerId = x.BuyerId,
                    SellerId = x.SellerId,
                },
                Payment x => new CommerceChange
                {
                    Topic = "orders",
                    OrderId = x.OrderId,
                    BuyerId = x.UserId,
                },
                Refund x => new CommerceChange { Topic = "returns", OrderId = x.OrderId },
                ReturnRequest x => new CommerceChange { Topic = "returns", OrderId = x.OrderId },
                ShippingInfo x => new CommerceChange
                {
                    Topic = "shipments",
                    OrderId = x.OrderId,
                    ShippingInfoId = x.Id > 0 ? x.Id : null,
                    ShipperId = x.ShipperId,
                },
                ShippingEvent x => new CommerceChange
                {
                    Topic = "shipments",
                    ShippingInfoId = x.ShippingInfoId,
                },
                Dispute x => new CommerceChange
                {
                    Topic = "disputes",
                    OrderId = x.OrderId,
                    DisputeId = x.Id > 0 ? x.Id : null,
                },
                DisputeEntry x => new CommerceChange
                {
                    Topic = "disputes",
                    DisputeId = x.DisputeId,
                },
                SellerAccount x => new CommerceChange { Topic = "finance", SellerId = x.SellerId },
                SellerPayout x => new CommerceChange
                {
                    Topic = "finance",
                    SellerAccountId = x.SellerAccountId,
                },
                SellerDebtEvent x => new CommerceChange
                {
                    Topic = "finance",
                    SellerAccountId = x.SellerAccountId,
                },
                SellerLevelEvaluation x => new CommerceChange
                {
                    Topic = "levels",
                    SellerId = x.SellerId,
                },
                SellerOrderFeedback x => new CommerceChange
                {
                    Topic = "levels",
                    SellerId = x.SellerId,
                    BuyerId = x.BuyerId,
                },
                Review x => new CommerceChange
                {
                    Topic = "product-feedback",
                    OrderId = x.OrderId,
                    BuyerId = x.ReviewerId,
                    SellerId = x.Product?.SellerId,
                },
                NotificationOutbox x => new CommerceChange
                {
                    Topic = "notifications",
                    BuyerId = x.RecipientUserId,
                    OrderId = x.RecipientUserId == null ? x.OrderId : null,
                    SellerId = x.RecipientUserId == null ? x.SellerId : null,
                },
                AddressChangeRequest x => new CommerceChange
                {
                    Topic = "orders",
                    OrderId = x.OrderId,
                    ShipperId = x.ExpectedShipperId,
                },
                UserNotification x when entry.State == EntityState.Modified => new CommerceChange
                {
                    Topic = "notification-inbox",
                    BuyerId = x.UserId,
                },
                _ => null,
            };
            if (signal != null)
            {
                signal.CreatedAt = DateTime.UtcNow;
                var content = BellNotificationText.Describe(entry);
                signal.NotificationTitle = content?.Title;
                signal.NotificationMessage = content?.Message;
                signals.Add(signal);
            }
        }
        CommerceChanges.AddRange(
            signals.DistinctBy(x =>
                (
                    x.Topic,
                    x.OrderId,
                    x.SellerId,
                    x.BuyerId,
                    x.SellerAccountId,
                    x.ShippingInfoId,
                    x.ShipperId,
                    x.DisputeId,
                    x.NotificationTitle,
                    x.NotificationMessage
                )
            )
        );
    }
}
