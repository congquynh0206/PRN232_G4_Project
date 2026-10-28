using System.Security.Claims;
using G4.Api.Controllers;
using G4.Api.Realtime;
using G4.Domain.Entities;
using G4.Infrastructure.Notifications;
using G4.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

internal static class BellNotificationChecks
{
    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        db.Users.AddRange(
            new User { Id = 1, Role = "buyer" },
            new User { Id = 2, Role = "seller" },
            new User { Id = 3, Role = "shipper" },
            new User { Id = 4, Role = "admin" },
            new User { Id = 5, Role = "buyer" },
            new User { Id = 6, Role = "shipper" }
        );
        db.OrderTables.Add(
            new OrderTable
            {
                Id = 10,
                BuyerId = 1,
                SellerId = 2,
                Status = "Paid",
            }
        );
        db.ShippingInfos.Add(
            new ShippingInfo
            {
                Id = 20,
                OrderId = 10,
                ShipperId = 3,
                Status = "InTransit",
                Direction = "Outbound",
            }
        );
        await db.SaveChangesAsync();
        var service = new BellNotificationService(db);
        var change = db.CommerceChanges.Local.Single(x => x.Topic == "shipments");
        var audiences = await CommerceChangeWorker.AudiencesAsync(db, change, default);
        await service.MaterializeAsync(change, audiences, default);
        await db.SaveChangesAsync();
        await service.MaterializeAsync(change, audiences, default);
        await db.SaveChangesAsync();
        Check(
            await db.UserNotifications.CountAsync() == 3,
            "retry duplicated rows or scope leaked"
        );
        Check(
            (await service.ListAsync(5, 1, 10, default)).TotalCount == 0,
            "another buyer received the order"
        );
        Check(
            (await service.ListAsync(6, 1, 10, default)).TotalCount == 0,
            "unassigned shipper received private tracking"
        );
        Check(
            (await service.ListAsync(4, 1, 10, default)).TotalCount == 0,
            "admin received an unrelated order alert"
        );
        var mine = await service.ListAsync(1, 1, 10, default);
        var ownId = mine.Items.Single().Id;
        var otherId = (await service.ListAsync(2, 1, 10, default)).Items.Single().Id;
        Check(mine.UnreadCount == 1, "listing marked a notification read");
        Check(!await service.MarkReadAsync(1, otherId, default), "foreign read accepted");
        Check(await service.MarkReadAsync(1, ownId, default), "own read rejected");
        var firstRead = (await service.ListAsync(1, 1, 10, default)).Items.Single().ReadAt;
        await service.MarkReadAsync(1, ownId, default);
        Check(
            (await service.ListAsync(1, 1, 10, default)).Items.Single().ReadAt == firstRead,
            "read retry changed the first-read timestamp"
        );
        Check(
            (await service.ListAsync(2, 1, 10, default)).UnreadCount == 1,
            "read changed another recipient"
        );
        Check(
            db.CommerceChanges.Local.Any(x =>
                x.Topic == "notification-inbox" && x.BuyerId == 1 && x.NotificationTitle == null
            ),
            "read did not invalidate own sessions"
        );

        var available = new CommerceChange
        {
            Topic = "shipments",
            OrderId = 10,
            NotificationTitle = "Vận đơn mới",
            NotificationMessage = "Đơn #10 · Đã tạo nhãn",
            CreatedAt = DateTime.UtcNow,
        };
        db.CommerceChanges.Add(available);
        await db.SaveChangesAsync();
        await service.MaterializeAsync(
            available,
            await CommerceChangeWorker.AudiencesAsync(db, available, default),
            default
        );
        await db.SaveChangesAsync();
        var generic = (await service.ListAsync(6, 1, 10, default)).Items.Single();
        Check(
            !generic.Message.Contains("10") && generic.TargetUrl == "/Shipper",
            "new-job alert exposed order identifiers"
        );

        var old = await service.ListAsync(2, 1, 1, default);
        var newer = new CommerceChange
        {
            Topic = "finance",
            SellerId = 2,
            NotificationTitle = "Ví cập nhật",
            NotificationMessage = "Số dư thay đổi",
            CreatedAt = DateTime.UtcNow,
        };
        db.CommerceChanges.Add(newer);
        await db.SaveChangesAsync();
        await service.MaterializeAsync(
            newer,
            await CommerceChangeWorker.AudiencesAsync(db, newer, default),
            default
        );
        await db.SaveChangesAsync();
        await service.MarkAllReadAsync(2, old.LatestId, default);
        var after = await service.ListAsync(2, 99, 1, default);
        Check(
            after.UnreadCount == 1 && after.Page == after.TotalCount,
            "mark-all swallowed a new arrival or pagination count was page-local"
        );
        Check(
            (await service.ListAsync(4, 1, 10, default)).UnreadCount == 1,
            "admin finance alert missing or mark-all changed admin state"
        );

        var legacy = new CommerceChange
        {
            Topic = "orders",
            BuyerId = 1,
            CreatedAt = DateTime.UtcNow,
        };
        db.CommerceChanges.Add(legacy);
        await db.SaveChangesAsync();
        Check(
            (
                await service.MaterializeAsync(legacy, new HashSet<string> { "user:1" }, default)
            ).Count == 0,
            "legacy event backfilled fake unread history"
        );
        var beforeNoise = db.CommerceChanges.Local.Count(x => x.NotificationTitle != null);
        var shipment = await db.ShippingInfos.SingleAsync();
        shipment.FailureReason = "Internal note only";
        await db.SaveChangesAsync();
        Check(
            db.CommerceChanges.Local.Count(x => x.NotificationTitle != null) == beforeNoise,
            "internal field update created a bell alert"
        );
        db.Payments.Add(
            new Payment
            {
                OrderId = 10,
                UserId = 1,
                Status = "Verifying",
                Method = "PayPal",
            }
        );
        await db.SaveChangesAsync();
        Check(
            db.CommerceChanges.Local.Any(x =>
                x.NotificationMessage != null
                && x.NotificationMessage.Contains("chưa xác nhận thành công")
            ),
            "Verifying notification claimed success"
        );

        var controller = new BellNotificationsController(db, new TestEnvironment())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [
                                new Claim(ClaimTypes.NameIdentifier, "1"),
                                new Claim(ClaimTypes.Role, "buyer"),
                            ],
                            "test"
                        )
                    ),
                },
            },
        };
        Check(
            await controller.Read(otherId, default) is NotFoundResult,
            "controller accepted another account notification"
        );
        Check(
            await controller.Read(ownId, default) is NoContentResult,
            "controller read retry failed"
        );
        Console.WriteLine(
            "Bell inbox: recipient isolation, retry, read state, watermark, paging and snapshots passed"
        );
    }

    internal static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception("Bell: " + message);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
