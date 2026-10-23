using G4.Domain.Entities;
using G4.Infrastructure.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class SellerFundsChecks
{
    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        var now = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);
        for (var id = 1; id <= 7; id++)
        {
            db.OrderTables.Add(
                new OrderTable
                {
                    Id = id,
                    SellerId = id == 7 ? 99 : 2,
                    BuyerId = 1,
                    Status = id == 2 ? "Paid" : "Delivered",
                    OrderDate = now,
                }
            );
            db.SellerSettlements.Add(
                new SellerSettlement
                {
                    Id = id,
                    SellerId = id == 7 ? 99 : 2,
                    OrderId = id,
                    SellerAccountId = id == 7 ? 99 : 1,
                    Status =
                        id is 3 or 6 or 7 ? "OnHold"
                        : id == 4 ? "PartiallyRefunded"
                        : id == 5 ? "Available"
                        : "Processing",
                    ProcessingAmount =
                        id == 1 ? 87.7m
                        : id == 2 ? 45m
                        : id == 4 ? 5m
                        : 0,
                    SellerLevelSnapshot = id == 2 ? 2 : 1,
                    HoldPolicyVersion = 1,
                    HoldSecondsSnapshot =
                        id == 1 ? 30
                        : id == 2 ? 180
                        : 300,
                    HoldStartedAt = id == 2 ? null : now,
                    ReleaseAt = id == 2 ? null : now.AddSeconds(id == 1 ? 30 : 300),
                    CreatedAt = now,
                }
            );
        }
        void Entry(long id, int settlement, string type, decimal amount) =>
            db.FinancialTransactions.Add(
                new FinancialTransaction
                {
                    Id = id,
                    SettlementId = settlement,
                    SellerAccountId = settlement == 7 ? 99 : 1,
                    OrderId = settlement,
                    Type = type,
                    Amount = amount,
                    EntryKey = $"test:{id}",
                    CreatedAt = now,
                }
            );
        Entry(1, 3, "FundHold", 999);
        Entry(2, 3, "FundHoldConsumed", -100);
        Entry(3, 3, "FundHold", 60);
        Entry(4, 3, "FundHoldConsumed", -10);
        Entry(5, 3, "HoldReleased", 5);
        Entry(6, 4, "FundHold", 30);
        Entry(7, 4, "FundHoldConsumed", -20);
        Entry(8, 5, "FundHold", 100);
        Entry(9, 6, "FundHold", 100);
        Entry(10, 6, "FundHoldConsumed", -100);
        Entry(11, 7, "FundHold", 999);
        db.Disputes.Add(
            new Dispute
            {
                Id = 1,
                OrderId = 3,
                WorkflowEnabled = true,
                IsOpen = true,
                Status = "AwaitingSeller",
                SellerResponseDueAt = now.AddSeconds(45),
            }
        );
        db.ReturnRequests.Add(
            new ReturnRequest
            {
                Id = 1,
                OrderId = 4,
                Status = "Requested",
            }
        );
        await db.SaveChangesAsync();
        var originalEntries = await db.FinancialTransactions.CountAsync();
        var first = await SellerFundsQuery.ReadAsync(db, 2, 1, 2);
        Check(
            first.TotalCount == 4 && first.Items.Select(x => x.OrderId).SequenceEqual([4, 3]),
            "fund pages exclude other sellers, released balances and fully consumed holds"
        );
        var partial = first.Items[0];
        Check(
            partial.Processing == 5 && partial.OnHold == 10 && partial.BlockReason == "Return",
            "processing and held amounts stay separate after partial consumption"
        );
        var disputed = first.Items[1];
        Check(
            disputed.OnHold == 45
                && disputed.DisputeId == 1
                && disputed.ResponseDueAt == now.AddSeconds(45)
                && disputed.ResponseKind == "seller-response"
                && disputed.BlockReason == "Dispute",
            "latest hold less consumption/releases and actual case deadline are returned"
        );
        var second = await SellerFundsQuery.ReadAsync(db, 2, 999, 2);
        Check(
            second.Page == 2 && second.Items[0].ReleaseAt == null,
            "unshipped funds have no invented deadline and page is clamped"
        );
        Check(
            second.Items[1].HoldSeconds == 30 && second.Items[1].ReleaseAt == now.AddSeconds(30),
            "old hold snapshot is preserved instead of applying current 300-second policy"
        );
        var own = await SellerFundsQuery.ReadAsync(db, 99, 1, 10);
        Check(
            own.TotalCount == 1 && own.Items.Single().OnHold == 999,
            "another seller can only read their own funds"
        );
        var hidden = await SellerFundsQuery.ReadAsync(db, 2, 1, 10, orderId: 7);
        Check(
            hidden.TotalCount == 0 && hidden.Items.Count == 0,
            "order detail lookup cannot cross seller ownership"
        );
        Check(
            await db.FinancialTransactions.CountAsync() == originalEntries
                && db.ChangeTracker.Entries().All(x => x.State == EntityState.Unchanged),
            "query never changes ledger, money, snapshots or workflow"
        );
        await using var sqlDb = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=unused;Database=translation-test;Integrated Security=true")
                .Options
        );
        var sql = SellerFundsQuery
            .Pending(sqlDb, 2)
            .OrderByDescending(x => x.Settlement.Id)
            .Take(10)
            .ToQueryString();
        var returned = await db.ReturnRequests.SingleAsync();
        returned.Status = "ReturnShipping";
        db.ShippingInfos.Add(
            new ShippingInfo
            {
                OrderId = 4,
                Direction = "Return",
                Status = "Delivered",
                DeliveredAt = now,
            }
        );
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Returns:ReceiptSeconds"] = "45",
                    ["Disputes:ReturnReceiptSeconds"] = "90",
                }
            )
            .Build();
        var receipt = await SellerFundsQuery.ReadAsync(db, 2, 1, 10, orderId: 4, config: config);
        Check(
            receipt.Items.Single().ResponseDueAt == now.AddSeconds(45),
            "legacy receipt without a saved deadline uses the actual server fallback"
        );
        db.Disputes.Add(
            new Dispute
            {
                OrderId = 4,
                WorkflowEnabled = true,
                IsOpen = true,
                Status = "ExecutingAgreement",
                Proposal = "ReturnRefund",
            }
        );
        await db.SaveChangesAsync();
        var agreement = await SellerFundsQuery.ReadAsync(db, 2, 1, 10, orderId: 4, config: config);
        Check(
            agreement.Items.Single().ResponseDueAt == now.AddSeconds(90)
                && agreement.Items.Single().ResponseKind == "return-agreement",
            "agreement countdown follows dispute receipt policy instead of ordinary receipt"
        );
        Check(
            sql.Contains("FundHoldConsumed")
                && sql.Contains("HoldReleased")
                && sql.Contains("MAX("),
            "actual pending funds query translates for SQL Server"
        );
        Console.WriteLine(
            "Seller funds: ownership, pagination, remaining holds, snapshots, blocked cases and read-only queries passed"
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
