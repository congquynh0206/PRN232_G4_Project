using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class CatalogController(
    ApplicationDbContext db,
    ICheckoutService checkout,
    ISellerFinanceService finance,
    IHostEnvironment environment
) : ApiControllerBase(environment)
{
    [HttpPost("seller/products")]
    public async Task<IActionResult> Publish(SellerProductInput input, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        if (
            string.IsNullOrWhiteSpace(input.Title)
            || input.Title.Length > 100
            || input.Price <= 0
            || input.Price > 99999999.99m
            || decimal.Round(input.Price, 2) != input.Price
            || input.WeightKg is < .001m or > 10000m
            || decimal.Round(input.WeightKg, 3) != input.WeightKg
            || input.Quantity <= 0
        )
            return BadRequest(
                new
                {
                    error = "Tiêu đề tối đa 100 ký tự; giá và tồn kho cần dương, khối lượng từ 0,001 đến 10.000 kg.",
                }
            );
        if (
            input.CategoryId != null
            && !await db.Categories.AnyAsync(c => c.Id == input.CategoryId, ct)
        )
            return BadRequest(new { error = "Danh mục không tồn tại." });
        await using var tx =
            db.Database.IsRelational() && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable,
                    ct
                )
                : null;
        await finance.ValidateNewSaleAsync(CurrentUserId, ct);
        var product = new Product
        {
            SellerId = CurrentUserId,
            Title = input.Title.Trim(),
            Description = input.Description,
            Price = input.Price,
            WeightKg = input.WeightKg,
            CategoryId = input.CategoryId,
            IsAuction = false,
            ImmediatePaymentRequired = input.ImmediatePaymentRequired,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        db.Inventories.Add(
            new Inventory
            {
                ProductId = product.Id,
                Quantity = input.Quantity,
                LastUpdated = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync(ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return Ok(
            new
            {
                product.Id,
                product.Title,
                product.Price,
                product.WeightKg,
                product.ImmediatePaymentRequired,
            }
        );
    }

    [HttpGet("seller/products")]
    public async Task<IActionResult> SellerProducts(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        return Ok(await ProductList(CurrentUserId).ToListAsync(ct));
    }

    [HttpPost("seller/products/{id:int}/payment-settings")]
    public async Task<IActionResult> Settings(
        int id,
        ProductPaymentSettings input,
        CancellationToken ct
    )
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        if (input.Price <= 0 || input.Price > 99999999.99m
            || decimal.Round(input.Price, 2) != input.Price)
            return BadRequest(new { error = "Invalid price" });
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct)
            : null;
        await G4.Infrastructure.Promotions.PromotionLifecycle.LockReservationsAsync(db, ct);
        var product = await db.Products.SingleOrDefaultAsync(
            p => p.Id == id && p.SellerId == CurrentUserId, ct);
        if (product == null)
            return NotFound();
        if (product.IsAuction == true)
            return BadRequest(new { error = "Buy It Now configuration is for fixed-price products." });
        if (product.Price != input.ExpectedPrice)
            return Conflict(new { error = "Price changed. Reload before editing." });
        product.Price = input.Price;
        product.ImmediatePaymentRequired = input.ImmediatePaymentRequired;
        product.IsListed = input.IsListed;
        await db.SaveChangesAsync(ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return Ok(new { product.Id, product.ImmediatePaymentRequired, product.IsListed });
    }

    [HttpGet("catalog/products")]
    public async Task<IActionResult> Products(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await ProductList(null).Take(100).ToListAsync(ct));
    }

    [HttpGet("catalog/products/{id:int}")]
    public async Task<IActionResult> Product(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        var product = await ProductList(null, id).SingleOrDefaultAsync(ct);
        if (product == null)
            return NotFound(new { error = "Sản phẩm không còn được bán." });
        return Ok(product);
    }

    // Newest products first, so a just-published product is never cut off by Take(100).
    // Filter before the projection: SQL cannot filter on the projected record's subqueries.
    private IQueryable<ProductListing> ProductList(int? sellerId, int? productId = null) =>
        db.Products.AsNoTracking()
            .Where(p => p.IsAuction != true && p.Price != null
                && (sellerId != null || p.IsListed)
                && (sellerId == null || p.SellerId == sellerId)
                && (productId == null || p.Id == productId))
            .OrderByDescending(p => p.Id)
            .Select(p => new ProductListing(
                p.Id, p.Title, p.Description, p.Price, p.WeightKg,
                p.ImmediatePaymentRequired, p.IsListed,
                db.Inventories.Where(i => i.ProductId == p.Id).Sum(i => i.Quantity) ?? 0,
                p.SellerId,
                db.Users.Where(u => u.Id == p.SellerId).Select(u => u.Username).FirstOrDefault(),
                p.Images,
                p.AllowOffers
            ));

    private sealed record ProductListing(
        int Id, string? Title, string? Description, decimal? Price, decimal? WeightKg,
        bool ImmediatePaymentRequired, bool IsListed, int Available,
        int? SellerId, string? SellerName, string? ImageUrl, bool AllowOffers
    );

    [HttpGet("catalog/random")]
    public async Task<IActionResult> RandomCart(
        [FromQuery] int count = 3,
        CancellationToken ct = default
    )
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        var products = await checkout.RandomProductsAsync(count, ct);
        var ids = products.Select(p => p.Id).ToArray();
        var stock = await db
            .Inventories.Where(i => ids.Contains(i.ProductId ?? 0))
            .ToDictionaryAsync(i => i.ProductId!.Value, ct);
        return Ok(
            products.Select(p => new
            {
                productId = p.Id,
                title = p.Title,
                price = p.Price,
                p.WeightKg,
                imageUrl = p.Images,
                quantity = Random.Shared.Next(1, Math.Min(3, stock[p.Id].Quantity!.Value) + 1),
                available = stock[p.Id].Quantity,
            })
        );
    }
}
