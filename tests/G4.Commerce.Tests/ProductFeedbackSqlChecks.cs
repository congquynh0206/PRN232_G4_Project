using System.Text.RegularExpressions;
using G4.Domain.Entities;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

internal static class ProductFeedbackSqlChecks
{
    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("G4_PRODUCT_FEEDBACK_SQL_CONNECTION") ?? throw new Exception("Product feedback QA connection required");
        if (new SqlConnectionStringBuilder(connection).InitialCatalog != "G4_ProductFeedback_Test_20261017")
            throw new Exception("Only G4_ProductFeedback_Test_20261017 is allowed");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        var clock = new ProductFeedbackChecks.Clock();
        void Check(bool ok, string message) => ProductFeedbackChecks.Check(ok, message);
        await using (var db = new ApplicationDbContext(options))
        {
            var creator = db.Database.GetService<IRelationalDatabaseCreator>();
            if (!await creator.ExistsAsync()) await creator.CreateAsync();
            await using var sql = new SqlConnection(connection); await sql.OpenAsync();
            await using var exists = new SqlCommand("SELECT OBJECT_ID(N'[User]')", sql);
            if (await exists.ExecuteScalarAsync() is DBNull or null)
            {
                var baseline = Regex.Replace(await File.ReadAllTextAsync("database/baseline.sql"), @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE CloneEbayDB;)\s*$", "");
                foreach (var batch in Regex.Split(baseline, @"(?im)^\s*GO\s*$"))
                    if (!string.IsNullOrWhiteSpace(batch)) { await using var cmd = new SqlCommand(batch, sql); await cmd.ExecuteNonQueryAsync(); }
                await db.Database.GetService<IMigrator>().MigrateAsync("20261015161703_AddPayPalCartAndRealtime");
                await using var sentinel = new SqlCommand(@"INSERT INTO [User] ([role],[email],[username]) VALUES (N'seller',N'legacy-product-review@example.test',N'Legacy shop');
                    DECLARE @seller int = SCOPE_IDENTITY();
                    INSERT INTO Product (sellerId,title,price) VALUES (@seller,N'Legacy product',10);
                    DECLARE @product int = SCOPE_IDENTITY();
                    INSERT INTO Review (productId,reviewerId,rating,comment,createdAt) VALUES (@product,@seller,4,N'Legacy product review preserved','2026-10-01');", sql);
                await sentinel.ExecuteNonQueryAsync();
            }
            await db.Database.MigrateAsync();
            Check(await db.Reviews.AnyAsync(r => r.Comment == "Legacy product review preserved" && r.Rating == 4 && r.OrderId == null && r.RequestKey == null), "migration preserves old Review data without claiming verified purchase");
        }
        ProductFeedbackChecks.Fixture f;
        await using (var db = new ApplicationDbContext(options)) f = await ProductFeedbackChecks.Seed(db);
        async Task<int> Submit()
        {
            await using var db = new ApplicationDbContext(options);
            return (await new ProductFeedbackService(db, clock).SubmitAsync(f.Buyer, f.Order, f.Product, new(5, "SQL review <script>", "sql-review"))).Id;
        }
        var reviews = await Task.WhenAll(Submit(), Submit()); Check(reviews[0] == reviews[1], "SQL concurrent buyer retry saves once");
        async Task<int> Reply()
        {
            await using var db = new ApplicationDbContext(options);
            return (await new ProductFeedbackService(db, clock).ReplyAsync(f.Seller, reviews[0], new("SQL reply <img>", "sql-reply"))).Id;
        }
        var replies = await Task.WhenAll(Reply(), Reply()); Check(replies[0] == replies[1], "SQL concurrent reply saves once");
        await using (var db = new ApplicationDbContext(options))
        {
            var service = new ProductFeedbackService(db, clock);
            var all = await service.ProductsAsync(f.Seller, new() { PageSize = 1 });
            Check(all.Summary.ReviewCount == 2 && all.Summary.AverageRating == 3.5m && all.Summary.UnansweredCount == 1 && all.Items.Single().ReviewCount == 2, "SQL grouped summaries translate and total beyond paging");
            var filtered = await service.ProductsAsync(f.Seller, new() { Rating = 5, Reply = "answered", From = new(ProductFeedbackChecks.Now), To = new(ProductFeedbackChecks.Now.AddDays(1)) });
            Check(filtered.TotalCount == 1 && filtered.Summary.ReviewCount == 1, "SQL combined date/rating/reply filters");
            var page = await service.ReviewsAsync(f.Seller, f.Product, new() { PageSize = 1, Page = 2 });
            Check(page.Items.Single().Id == f.LegacyReview && !page.Items.Single().VerifiedPurchase, "SQL stable paging keeps legacy review visible");
            await ProductFeedbackChecks.Reject<KeyNotFoundException>(() => service.ReplyAsync(f.OtherSeller, reviews[0], new("bad owner", "bad")));
            await ProductFeedbackChecks.Reject<InvalidOperationException>(() => service.ReplyAsync(f.Seller, reviews[0], new("new content", "sql-reply")));
        }
        await using (var db = new ApplicationDbContext(options))
        {
            db.ProductReviewReplies.Add(new ProductReviewReply { ReviewId = reviews[0], SellerId = f.Seller, Comment = "duplicate", RequestKey = Guid.NewGuid().ToString(), CreatedAt = ProductFeedbackChecks.Now });
            await ProductFeedbackChecks.Reject<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = new ApplicationDbContext(options))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await new ProductFeedbackService(db, clock).ReplyAsync(f.Seller, f.LegacyReview, new("rolled back", "rollback"));
            await tx.RollbackAsync();
        }
        await using (var db = new ApplicationDbContext(options))
        {
            Check(!await db.ProductReviewReplies.AnyAsync(r => r.ReviewId == f.LegacyReview), "SQL rollback does not persist reply");
            var seller = await db.Users.SingleAsync(x => x.Id == f.Seller); var buyer = await db.Users.SingleAsync(x => x.Id == f.Buyer);
            var other = await db.Users.SingleAsync(x => x.Id == f.OtherSeller);
            var password = "FeedbackDemo123!"; var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
            seller.Password = buyer.Password = other.Password = $"PBKDF2$210000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
            // A second purchased product supplies a genuine review submission path for browser QA.
            var extra = new Product { SellerId = f.Seller, Title = "Camera QA", Price = 20m, WeightKg = .6m, IsAuction = false };
            db.Products.Add(extra); await db.SaveChangesAsync();
            var extraOrder = new OrderTable { BuyerId = f.Buyer, SellerId = f.Seller, Status = "Delivered", OrderDate = ProductFeedbackChecks.Now.AddDays(-1),
                UpdatedAt = ProductFeedbackChecks.Now, TotalPrice = 20m, Subtotal = 20m };
            db.OrderTables.Add(extraOrder); await db.SaveChangesAsync();
            db.OrderItems.Add(new OrderItem { OrderId = extraOrder.Id, ProductId = extra.Id, Quantity = 1, UnitPrice = 20m, ProductTitleSnapshot = extra.Title });
            db.Payments.Add(new Payment { OrderId = extraOrder.Id, UserId = f.Buyer, Amount = 20m, Method = "Card", Status = "Succeeded",
                CreatedAt = ProductFeedbackChecks.Now.AddDays(-1), PaidAt = ProductFeedbackChecks.Now.AddDays(-1), IdempotencyKey = "browser-feedback-" + Guid.NewGuid().ToString("N") });
            db.Reviews.Add(new Review { ProductId = extra.Id, ReviewerId = f.OtherBuyer, Rating = 3, Comment = "Camera needs clearer instructions", CreatedAt = ProductFeedbackChecks.Now.AddHours(-1) });
            await db.SaveChangesAsync();
            Directory.CreateDirectory("artifacts/product-feedback-qa");
            await File.WriteAllTextAsync("artifacts/product-feedback-qa/fixture.json", System.Text.Json.JsonSerializer.Serialize(new { sellerEmail = seller.Email, buyerEmail = buyer.Email, otherSellerEmail = other.Email,
                orderId = extraOrder.Id, productId = extra.Id, existingProductId = f.Product, existingReviewId = reviews[0] }));
        }
        Console.WriteLine("Product feedback SQL checks passed: legacy migration, SQL grouping/filtering, concurrent idempotency, unique constraints, ownership and rollback. QA fixture written.");
    }
}
