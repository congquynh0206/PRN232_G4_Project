using System.Data;
using System.Data.Common;
using System.Text.Json;
using G4.Domain.Entities;
using G4.Infrastructure.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

internal static class AdminOrderLookupChecks
{
    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        var instant = new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Unspecified);
        db.Users.AddRange(
            new User
            {
                Id = 1,
                Username = "Buyer",
                Role = "buyer",
            },
            new User
            {
                Id = 2,
                Username = "Seller",
                Role = "seller",
            }
        );
        foreach (var id in new[] { 100, 200, 300 })
            db.OrderTables.Add(
                new OrderTable
                {
                    Id = id,
                    BuyerId = 1,
                    SellerId = 2,
                    Status = "Paid",
                    TotalPrice = 100m,
                    OrderDate = instant,
                }
            );
        db.Payments.AddRange(
            new Payment
            {
                Id = 100,
                OrderId = 200,
                Status = "Succeeded",
                Amount = 100m,
                Method = "PayPal",
                CreatedAt = instant,
                PaidAt = instant,
                ProviderTransactionId = "CAPTURE-ABC",
                ProviderOrderId = "PROVIDER-ORDER",
            },
            new Payment
            {
                Id = 101,
                OrderId = 300,
                Status = "Succeeded",
                Amount = 100m,
                CreatedAt = instant,
                ProviderTransactionId = "XCAPTURE-ABCX",
            },
            new Payment
            {
                Id = 102,
                OrderId = 200,
                Status = "Failed",
                Amount = 100m,
                CreatedAt = instant,
            }
        );
        db.Refunds.AddRange(
            new Refund
            {
                Id = 100,
                OrderId = 300,
                PaymentId = 101,
                Amount = 30m,
                Status = "Pending",
                CreatedAt = instant,
                ProviderRefundId = "XREFUND-ABCX",
            },
            new Refund
            {
                Id = 101,
                OrderId = 200,
                PaymentId = 100,
                Amount = 20m,
                Status = "Succeeded",
                CreatedAt = instant,
                CompletedAt = instant,
                ProviderRefundId = "REFUND-ABC",
            },
            new Refund
            {
                Id = 102,
                OrderId = 200,
                PaymentId = 100,
                Amount = 40m,
                Status = "Failed",
                CreatedAt = instant,
            }
        );
        db.ShippingInfos.Add(
            new ShippingInfo
            {
                Id = 1,
                OrderId = 200,
                Direction = "Outbound",
                CreatedAt = instant,
                DeliveredAt = instant,
                Status = "Delivered",
            }
        );
        db.ShippingEvents.Add(
            new ShippingEvent
            {
                Id = 1,
                ShippingInfoId = 1,
                Status = "Delivered",
                OccurredAt = instant,
            }
        );
        db.Disputes.Add(
            new Dispute
            {
                Id = 1,
                OrderId = 200,
                CreatedAt = instant,
                EscalatedAt = instant,
            }
        );
        db.IntegrationLogs.AddRange(
            new IntegrationLog
            {
                Id = 1,
                OrderId = 200,
                ProviderReference = "LOG-ABC",
                EntityId = "ENTITY-ABC",
                CreatedAt = instant,
            },
            new IntegrationLog
            {
                Id = 2,
                OrderId = 300,
                ProviderReference = "XLOG-ABCX",
                EntityId = "XENTITY-ABCX",
                CreatedAt = instant,
            }
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new AdminOrderLookupService(db);
        async Task Orders(string search, string expected, string type = "Auto")
        {
            var result = await service.SearchAsync(new(Search: search, SearchType: type));
            Check(
                string.Join(',', result.Items.Select(x => x.OrderId)) == expected,
                $"search {type}/{search} must return {expected}"
            );
        }
        await Orders("#100", "100");
        await Orders("100", "100");
        await Orders("order:100", "100");
        await Orders("payment:100", "200");
        await Orders("refund:100", "300");
        await Orders("100", "200", "Payment");
        await Orders("100", "300", "Refund");
        await Orders(" CAPTURE-ABC ", "200");
        await Orders("CAPTURE", "");
        await Orders("PROVIDER-ORDER", "200");
        await Orders("REFUND-ABC", "200");
        await Orders("LOG-ABC", "200");
        await Orders("ENTITY-ABC", "200");
        await Reject(() => service.SearchAsync(new(Search: "#abc")));
        await Reject(() => service.SearchAsync(new(Search: "#100", SearchType: "Payment")));
        await Reject(() => service.SearchAsync(new(Search: "0", SearchType: "Refund")));
        await Reject(() => service.SearchAsync(new(SearchType: "unknown")));
        var failed = await service.SearchAsync(new(QuickFilter: "PaymentFailed"));
        Check(
            failed.Items.Single().OrderId == 200 && failed.Items.Single().HasPaymentFailure,
            "successful retry remains in failure-history filter"
        );
        var page = await service.SearchAsync(new(Page: 2, PageSize: 1));
        Check(
            page.TotalCount == 3 && page.Items.Single().OrderId == 200,
            "one row per order with stable paging"
        );
        var detail = await service.DetailAsync(200);
        Check(
            detail.CollectedAmount == 100m
                && detail.RefundedAmount == 20m
                && detail.RemainingAmount == 80m,
            "only successful transactions contribute to lifetime totals"
        );
        Check(
            detail.Order.OrderDate!.Value.Kind == DateTimeKind.Utc,
            "SQL-style order timestamp has UTC kind"
        );
        Check(
            detail.Payments.All(x => x.CreatedAt.Kind == DateTimeKind.Utc)
                && detail.Payments[0].PaidAt!.Value.Kind == DateTimeKind.Utc,
            "payment timestamps UTC"
        );
        Check(
            detail.Refunds.All(x => x.CreatedAt.Kind == DateTimeKind.Utc)
                && detail.Refunds.Single(x => x.Status == "Succeeded").CompletedAt!.Value.Kind
                    == DateTimeKind.Utc,
            "refund timestamps UTC"
        );
        Check(
            detail.Shipments.Single().CreatedAt.Kind == DateTimeKind.Utc
                && detail.Shipments.Single().DeliveredAt!.Value.Kind == DateTimeKind.Utc
                && detail.Shipments.Single().Events.Single().OccurredAt.Kind == DateTimeKind.Utc,
            "shipment and event timestamps UTC"
        );
        Check(detail.Disputes.Single().CreatedAt.Kind == DateTimeKind.Utc, "dispute timestamp UTC");
        Check(
            JsonSerializer.Serialize(detail).Contains("2026-10-19T08:00:00Z"),
            "JSON explicitly identifies UTC instead of relying on browser timezone"
        );
        Check(
            !db.ChangeTracker.HasChanges(),
            "lookup must not write data or change stored timestamps"
        );

        var capture = new LookupSqlCapture();
        await using var sqlDb = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=unused;Database=translation-test;Integrated Security=true")
                .AddInterceptors(new SuppressSellerSqlConnection(), capture)
                .Options
        );
        var sqlService = new AdminOrderLookupService(sqlDb);
        foreach (
            var request in new[]
            {
                new AdminOrderLookupQuery(Search: "#100"),
                new(Search: "payment:100"),
                new(Search: "refund:100"),
                new(Search: "CAPTURE-ABC"),
            }
        )
            await sqlService.SearchAsync(request);
        Check(
            capture.Sql.Count >= 8
                && capture.Sql.All(x =>
                    !x.Contains("CHARINDEX", StringComparison.OrdinalIgnoreCase)
                    && !x.Contains("LIKE", StringComparison.OrdinalIgnoreCase)
                ),
            "SQL Server translates exact-match lookups without substring predicates"
        );
        Console.WriteLine(
            "Admin lookup: unambiguous IDs, exact provider references, retry history, lifetime totals, UTC serialization and SQL translation passed"
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private static async Task Reject(Func<Task<AdminOrderLookupPage>> work)
    {
        try
        {
            await work();
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new Exception("Invalid lookup input was accepted");
    }

    private sealed class LookupSqlCapture : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken ct = default
        )
        {
            Sql.Add(command.CommandText);
            var table = new DataTable();
            table.Columns.Add("Value", typeof(int));
            if (
                command
                    .CommandText.TrimStart()
                    .StartsWith("SELECT COUNT(*)", StringComparison.Ordinal)
            )
                table.Rows.Add(0);
            return ValueTask.FromResult(
                InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader())
            );
        }
    }
}
