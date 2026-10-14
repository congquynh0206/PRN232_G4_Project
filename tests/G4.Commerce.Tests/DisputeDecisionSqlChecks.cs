using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Disputes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

internal static class DisputeDecisionSqlChecks
{
    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("G4_DECISION_SQL_TEST_CONNECTION") ?? throw new Exception("Decision QA connection required");
        if (new SqlConnectionStringBuilder(connection).InitialCatalog != "G4_AdminDecision_Test_20261014")
            throw new Exception("Dedicated decision QA database required");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        await BootstrapAsync();
        var clock = TimeProvider.System;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Returns:ReceiptSeconds"] = "45" }).Build();
        var gateway = new Gateway();
        int buyerId, sellerId, adminId, productId;
        var sellerEmail = $"decision-seller-{Guid.NewGuid():N}@example.test";
        await using (var db = new ApplicationDbContext(options))
        {
            buyerId = (await db.Users.SingleAsync(x => x.Email == "buyer@example.test")).Id;
            adminId = (await db.Users.SingleAsync(x => x.Email == "admin@example.test")).Id;
            var seller = new User { Role = "seller", Username = "Decision QA seller", Email = sellerEmail,
                Password = (await db.Users.SingleAsync(x => x.Email == "seller@example.test")).Password };
            db.Users.Add(seller); await db.SaveChangesAsync(); sellerId = seller.Id;
            db.SellerAccounts.Add(new SellerAccount { SellerId = sellerId, Level = 3, MonthlySalesLimit = 1000000m,
                SalesMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1) });
            await db.SaveChangesAsync(); productId = (await db.Products.FirstAsync()).Id;
        }
        async Task<(int orderId, int caseId, int evidenceId)> AddCase(bool returned)
        {
            await using var db = new ApplicationDbContext(options);
            var order = new OrderTable { BuyerId = buyerId, SellerId = sellerId, Status = "Delivered", OrderDate = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow, TotalPrice = 95m, ShippingFee = 5m, Subtotal = 100m,
                PricingSchemaVersion = 1, PlatformSubsidy = 10m, SellerGrossSnapshot = 105m,
                TotalWeightKg = 1m, PickupStateSnapshot = "Hanoi", PickupCountrySnapshot = "Vietnam",
                DeliveryStateSnapshot = "HCM", DeliveryCountrySnapshot = "Vietnam", PickupAddressSnapshot = "Seller, Road, City, Hanoi, Vietnam",
                AddressSnapshot = "Buyer, Road, City, HCM, Vietnam" };
            order.OrderItems.Add(new OrderItem { ProductId = productId, ProductTitleSnapshot = "Sản phẩm QA phán quyết", Quantity = 1,
                UnitPrice = 100m, UnitWeightKgSnapshot = 1m, PlatformDiscountSnapshot = 10m, SellerIdSnapshot = sellerId });
            db.OrderTables.Add(order); await db.SaveChangesAsync();
            var payment = new Payment { OrderId = order.Id, UserId = buyerId, Status = "Succeeded", Method = "Card", Amount = 95m, PaidAt = DateTime.UtcNow };
            db.Payments.Add(payment); db.ShippingInfos.Add(new ShippingInfo { OrderId = order.Id, Direction = "Outbound", Status = "Delivered", DeliveredAt = DateTime.UtcNow,
                TrackingNumber = "QA-OUT-" + order.Id, PickupAddressSnapshot = order.PickupAddressSnapshot, DeliveryAddressSnapshot = order.AddressSnapshot });
            await db.SaveChangesAsync();
            var finance = new SellerFinanceService(db, config, clock); await finance.RecordSuccessfulPaymentAsync(order.Id, payment.Id);
            Dispute d;
            if (returned)
            {
                var returns = new ReturnService(db, gateway, new Carrier(), finance, config, clock);
                var q = await returns.QuoteAsync(order.Id, "WrongItem");
                var r = await returns.RequestPricedReturnAsync(order.Id, new("WrongItem", "Original buyer quote", Guid.NewGuid().ToString(), q.PricingFingerprint));
                await returns.ApproveAsync(r.Id);
                var parcel = await db.ShippingInfos.SingleAsync(x => x.OrderId == order.Id && x.Direction == "Return");
                parcel.Status = "Delivered"; parcel.DeliveredAt = DateTime.UtcNow; await db.SaveChangesAsync();
                d = await Service(db).ReportReturnIssueAsync(r.Id, sellerId, new("Seller reports returned condition", ["https://example.test/evidence"]));
            }
            else
            {
                d = await Service(db).OpenAsync(order.Id, buyerId, new("Buyer reports minor damage", ["https://example.test/evidence"]));
                d.Status = "Escalated"; d.EscalatedAt = DateTime.UtcNow; await db.SaveChangesAsync();
            }
            var evidenceId = await db.DisputeEntries.Where(x => x.DisputeId == d.Id && (x.Kind == "Evidence" || x.Kind == "ReturnIssue")).Select(x => x.Id).FirstAsync();
            return (order.Id, d.Id, evidenceId);
        }
        async Task<AdminDecisionInput> Input((int orderId, int caseId, int evidenceId) item, string responsibility)
        {
            await using var db = new ApplicationDbContext(options);
            var q = await Service(db).PreviewDecisionAsync(item.caseId, adminId, new("PartialRefund", responsibility, 40m));
            return new(q.Outcome, q.Responsibility, 40m, "Reviewed evidence and tracking", [item.evidenceId], Guid.NewGuid().ToString(), q.PricingFingerprint);
        }
        var a = await AddCase(true); var b = await AddCase(false);
        var inputA = await Input(a, "Buyer"); var inputB = await Input(b, "Seller");
        async Task Resolve(int id, AdminDecisionInput input)
        {
            await using var db = new ApplicationDbContext(options); await Service(db).ResolveDecisionAsync(id, adminId, input);
        }
        async Task Worker()
        {
            await using var db = new ApplicationDbContext(options); await Service(db).MaintainAsync();
        }
        await Task.WhenAll(Resolve(a.caseId, inputA), Resolve(a.caseId, inputA), Resolve(b.caseId, inputB), Worker());
        await Worker();
        await using (var db = new ApplicationDbContext(options))
        {
            Check(await db.DisputeDecisions.CountAsync(x => x.DisputeId == a.caseId || x.DisputeId == b.caseId) == 2, "one decision per case");
            Check(await db.Refunds.CountAsync(x => (x.OrderId == a.orderId || x.OrderId == b.orderId) && x.Status == "Succeeded" && x.Amount == 40m) == 2, "exact cash per case");
            var account = await db.SellerAccounts.SingleAsync(x => x.SellerId == sellerId);
            Check(account.AvailableBalance == 101.56m && account.OnHoldBalance == 0m && account.NegativeBalance == 0m, "concurrent same seller balances reconcile");
            Check(await db.FinancialTransactions.CountAsync(x => x.OrderId == a.orderId && x.Type == "ReturnShippingCost") == 1, "no duplicate carrier charge");
            Check((await db.ReturnRequests.SingleAsync(x => x.OrderId == a.orderId)).BuyerRefundAmount == 95m, "original quote preserved");
            Check(gateway.Calls == 2, "one gateway refund per decision");
        }
        var preview = await AddCase(true);
        Directory.CreateDirectory("artifacts/admin-decisions");
        await File.WriteAllTextAsync("artifacts/admin-decisions/qa-fixture.json", System.Text.Json.JsonSerializer.Serialize(new
            { previewOrderId = preview.orderId, caseId = preview.caseId, evidenceId = preview.evidenceId, sellerEmail, buyerEmail = "buyer@example.test" }));
        Console.WriteLine("SQL admin decisions: additive migration, duplicate submissions/worker, two cases one seller, one refund/fee charge and exact ledger passed");
        DisputeService Service(ApplicationDbContext db)
        {
            var finance = new SellerFinanceService(db, config, clock);
            return new(db, finance, new ReturnService(db, gateway, new Carrier(), finance, config, clock), config, clock);
        }
        async Task BootstrapAsync()
        {
            await using var db = new ApplicationDbContext(options);
            var creator = db.Database.GetService<IRelationalDatabaseCreator>(); if (!await creator.ExistsAsync()) await creator.CreateAsync();
            await using var sql = new SqlConnection(connection); await sql.OpenAsync();
            await using var exists = new SqlCommand("SELECT OBJECT_ID(N'[User]')", sql);
            if (await exists.ExecuteScalarAsync() is DBNull or null)
                await ExecuteAsync(Regex.Replace(await File.ReadAllTextAsync("database/baseline.sql"), @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE CloneEbayDB;)\s*$", ""));
            await db.Database.MigrateAsync();
            await ExecuteAsync((await File.ReadAllTextAsync("database/seed-data.sql")).Replace("USE [CloneEbayDB];", "USE [G4_AdminDecision_Test_20261014];"));
            async Task ExecuteAsync(string script)
            {
                foreach (var batch in Regex.Split(script, @"(?im)^\s*GO\s*$")) if (!string.IsNullOrWhiteSpace(batch))
                { await using var command = new SqlCommand(batch, sql); await command.ExecuteNonQueryAsync(); }
            }
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Carrier : ICarrierGateway
    { public Task<string> CreateLabelAsync(int orderId, string direction, string key, CancellationToken ct) => Task.FromResult(key); }
    private sealed class Gateway : IRefundGateway
    {
        public int Calls;
        public Task<string> RefundAsync(Payment p, decimal amount, string key, CancellationToken ct)
        { Interlocked.Increment(ref Calls); return Task.FromResult("SQL-" + key); }
    }
}
