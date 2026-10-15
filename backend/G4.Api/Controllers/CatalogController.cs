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
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 100 || input.Price <= 0
            || input.Price > 99999999.99m || decimal.Round(input.Price, 2) != input.Price
            || input.WeightKg is < .001m or > 10000m || decimal.Round(input.WeightKg, 3) != input.WeightKg
            || input.Quantity <= 0)
            return BadRequest(new { error = "Tiêu đề tối đa 100 ký tự; giá và tồn kho cần dương, khối lượng từ 0,001 đến 10.000 kg." });
        if (input.CategoryId != null && !await db.Categories.AnyAsync(c => c.Id == input.CategoryId, ct))
            return BadRequest(new { error = "Danh mục không tồn tại." });
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await finance.ValidateNewSaleAsync(CurrentUserId, ct);
        var product = new Product { SellerId = CurrentUserId, Title = input.Title.Trim(), Description = input.Description,
            Price = input.Price, WeightKg = input.WeightKg, CategoryId = input.CategoryId, IsAuction = false };
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        db.Inventories.Add(new Inventory { ProductId = product.Id, Quantity = input.Quantity, LastUpdated = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return Ok(new { product.Id, product.Title, product.Price, product.WeightKg });
    }

    [HttpGet("catalog/random")]
    public async Task<IActionResult> RandomCart([FromQuery] int count = 3, CancellationToken ct = default)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        var products = await checkout.RandomProductsAsync(count, ct);
        var ids = products.Select(p => p.Id).ToArray();
        var stock = await db.Inventories.Where(i => ids.Contains(i.ProductId ?? 0)).ToDictionaryAsync(i => i.ProductId!.Value, ct);
        return Ok(products.Select(p => new { productId = p.Id, title = p.Title, price = p.Price, p.WeightKg, imageUrl = p.Images, quantity = Random.Shared.Next(1, Math.Min(3, stock[p.Id].Quantity!.Value) + 1), available = stock[p.Id].Quantity }));
    }

    [HttpGet("addresses")]
    public async Task<IActionResult> Addresses(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await db.Addresses.Where(a => a.UserId == CurrentUserId).Select(a => new { a.Id, a.FullName, a.Street, a.City, a.State, a.Country, a.IsDefault })
            .ToListAsync(ct));
    }

    [HttpPost("addresses/{id:int}/edit")]
    public async Task<IActionResult> EditAddress(int id, AddressUpdateRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        if (new[]
        {
            request.FullName,
            request.Street,
            request.City,
            request.State,
            request.Country
        }.Any(string.IsNullOrWhiteSpace) || request.FullName.Length > 100 || request.Street.Length > 100 || request.City.Length > 50 || request.State.Length > 50 || request.Country.Length > 50)
            return BadRequest(new { error = "Address fields are required and must fit the allowed lengths" });
        var address = await db.Addresses.SingleOrDefaultAsync(a => a.Id == id && a.UserId == CurrentUserId, ct);
        if (address is null)
            return NotFound();
        address.FullName = request.FullName.Trim();
        address.Street = request.Street.Trim();
        address.City = request.City.Trim();
        address.State = request.State.Trim();
        address.Country = request.Country.Trim();
        await db.SaveChangesAsync(ct);
        return Ok(new { address.Id, address.FullName, address.Street, address.City, address.State, address.Country, address.IsDefault });
    }
}
