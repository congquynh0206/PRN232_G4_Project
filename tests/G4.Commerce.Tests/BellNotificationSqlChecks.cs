using System.Text.RegularExpressions;
using G4.Domain.Entities;
using G4.Infrastructure.Notifications;
using G4.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

internal static class BellNotificationSqlChecks
{
    public const string DatabaseName = "G4_NotificationBell_Test_20261027";

    public static async Task RunAsync()
    {
        var connection =
            Environment.GetEnvironmentVariable("G4_BELL_SQL_CONNECTION")
            ?? throw new Exception("G4_BELL_SQL_CONNECTION is required");
        var builder = new SqlConnectionStringBuilder(connection);
        if (builder.InitialCatalog != DatabaseName)
            throw new Exception("Only the dedicated bell QA database is allowed");
        var masterBuilder = new SqlConnectionStringBuilder(connection)
        {
            InitialCatalog = "master",
        };
        await using (var master = new SqlConnection(masterBuilder.ConnectionString))
        {
            await master.OpenAsync();
            await using var exists = new SqlCommand("SELECT DB_ID(@name)", master);
            exists.Parameters.AddWithValue("@name", DatabaseName);
            if (await exists.ExecuteScalarAsync() is null or DBNull)
            {
                var root = Path.GetFullPath(Environment.CurrentDirectory);
                var dir = Path.GetFullPath(Path.Combine(root, ".temp-appdata", "bell-sql"));
                if (
                    !dir.StartsWith(
                        root + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    throw new Exception("QA data path outside workspace");
                Directory.CreateDirectory(dir);
                var mdf = Path.Combine(dir, DatabaseName + ".mdf").Replace("'", "''");
                var ldf = Path.Combine(dir, DatabaseName + ".ldf").Replace("'", "''");
                await using var create = new SqlCommand(
                    $"CREATE DATABASE [{DatabaseName}] ON PRIMARY (NAME=N'{DatabaseName}',FILENAME=N'{mdf}',SIZE=32MB,FILEGROWTH=16MB) LOG ON (NAME=N'{DatabaseName}_log',FILENAME=N'{ldf}',SIZE=16MB,FILEGROWTH=16MB)",
                    master
                );
                await create.ExecuteNonQueryAsync();
            }
        }
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await using (var sql = new SqlConnection(connection))
        {
            await sql.OpenAsync();
            await using var exists = new SqlCommand("SELECT OBJECT_ID(N'[User]')", sql);
            if (await exists.ExecuteScalarAsync() is null or DBNull)
            {
                var baseline = Regex.Replace(
                    await File.ReadAllTextAsync("database/baseline.sql"),
                    @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE CloneEbayDB;)\s*$",
                    ""
                );
                await BatchesAsync(sql, baseline);
                await db
                    .Database.GetService<IMigrator>()
                    .MigrateAsync("20261020162647_UnifyPayoutWorkflowAndHistory");
                await using var legacy = new SqlCommand(
                    "INSERT CommerceChange (Topic,CreatedAt) VALUES (N'bell-legacy-qa','2026-10-01')",
                    sql
                );
                await legacy.ExecuteNonQueryAsync();
            }
            await db.Database.MigrateAsync();
            var seed = Regex.Replace(
                await File.ReadAllTextAsync("database/seed-data.sql"),
                @"(?im)^\s*USE \[CloneEbayDB\];\s*$",
                ""
            );
            await BatchesAsync(sql, seed);
        }
        var legacyRow = await db
            .CommerceChanges.AsNoTracking()
            .SingleAsync(x => x.Topic == "bell-legacy-qa");
        BellNotificationChecks.Check(
            legacyRow.NotificationTitle == null && legacyRow.CreatedAt == new DateTime(2026, 10, 1),
            "migration changed legacy event"
        );
        var buyer = await db
            .Users.Where(x => x.Email == "buyer@example.test")
            .Select(x => x.Id)
            .SingleAsync();
        var seller = await db
            .Users.Where(x => x.Email == "seller@example.test")
            .Select(x => x.Id)
            .SingleAsync();
        if (!await db.Users.AnyAsync(x => x.Email == "bell-other@example.test"))
        {
            var password = await db
                .Users.Where(x => x.Id == buyer)
                .Select(x => x.Password)
                .SingleAsync();
            db.Users.Add(
                new User
                {
                    Username = "bell-other",
                    Email = "bell-other@example.test",
                    Role = "buyer",
                    Password = password,
                }
            );
            await db.SaveChangesAsync();
        }
        var change = new CommerceChange
        {
            Topic = "orders",
            BuyerId = buyer,
            SellerId = seller,
            CreatedAt = DateTime.UtcNow,
            NotificationTitle = "Kiểm thử SQL",
            NotificationMessage = "Sự kiện QA đã commit.",
        };
        db.CommerceChanges.Add(change);
        await db.SaveChangesAsync();
        var audiences = new HashSet<string> { "user:" + buyer, "user:" + seller };
        var service = new BellNotificationService(db);
        await service.MaterializeAsync(change, audiences, default);
        await db.SaveChangesAsync();
        await service.MaterializeAsync(change, audiences, default);
        await db.SaveChangesAsync();
        BellNotificationChecks.Check(
            await db.UserNotifications.CountAsync(x => x.CommerceChangeId == change.Id) == 2,
            "SQL retry duplicated recipient events"
        );
        var item = await db
            .UserNotifications.AsNoTracking()
            .SingleAsync(x => x.CommerceChangeId == change.Id && x.UserId == buyer);
        await Task.WhenAll(
            Enumerable
                .Range(0, 2)
                .Select(async _ =>
                {
                    await using var concurrent = new ApplicationDbContext(options);
                    await new BellNotificationService(concurrent).MarkReadAsync(
                        buyer,
                        item.Id,
                        default
                    );
                })
        );
        var firstRead = (
            await db.UserNotifications.AsNoTracking().SingleAsync(x => x.Id == item.Id)
        ).ReadAt;
        await service.MarkReadAsync(buyer, item.Id, default);
        BellNotificationChecks.Check(
            firstRead != null
                && firstRead
                    == (
                        await db.UserNotifications.AsNoTracking().SingleAsync(x => x.Id == item.Id)
                    ).ReadAt,
            "SQL concurrent read/retry changed ReadAt"
        );
        var sellerItem = await db
            .UserNotifications.AsNoTracking()
            .SingleAsync(x => x.CommerceChangeId == change.Id && x.UserId == seller);
        BellNotificationChecks.Check(
            !await service.MarkReadAsync(buyer, sellerItem.Id, default),
            "SQL foreign notification was marked read"
        );
        await using (var rollback = new ApplicationDbContext(options))
        {
            await using var tx = await rollback.Database.BeginTransactionAsync();
            var rolledBack = new CommerceChange
            {
                Topic = "orders",
                BuyerId = buyer,
                CreatedAt = DateTime.UtcNow,
                NotificationTitle = "Không được tồn tại",
                NotificationMessage = "Rollback QA",
            };
            rollback.CommerceChanges.Add(rolledBack);
            await rollback.SaveChangesAsync();
            await new BellNotificationService(rollback).MaterializeAsync(
                rolledBack,
                audiences,
                default
            );
            await rollback.SaveChangesAsync();
            await tx.RollbackAsync();
            BellNotificationChecks.Check(
                !await db.UserNotifications.AnyAsync(x => x.CommerceChangeId == rolledBack.Id),
                "rollback left a phantom notification"
            );
        }
        await using (var duplicate = new ApplicationDbContext(options))
        {
            duplicate.UserNotifications.Add(
                new UserNotification
                {
                    UserId = buyer,
                    CommerceChangeId = change.Id,
                    Title = "Duplicate",
                    Message = "Duplicate QA",
                    TargetUrl = "/Buyer",
                    CreatedAt = DateTime.UtcNow,
                }
            );
            try
            {
                await duplicate.SaveChangesAsync();
                throw new Exception("Unique recipient/event constraint missing");
            }
            catch (DbUpdateException ex)
                when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { }
        }
        Console.WriteLine(
            "Bell SQL: migration preserved legacy, retry/unique key, concurrent read, scope and rollback passed"
        );
    }

    private static async Task BatchesAsync(SqlConnection sql, string script)
    {
        foreach (var batch in Regex.Split(script, @"(?im)^\s*GO\s*$"))
        {
            if (string.IsNullOrWhiteSpace(batch))
                continue;
            await using var command = new SqlCommand(batch, sql) { CommandTimeout = 120 };
            await command.ExecuteNonQueryAsync();
        }
    }
}
