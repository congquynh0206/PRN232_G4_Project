

using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class CatalogController(ApplicationDbContext db, ICheckoutService checkout, IHostEnvironment environment)
    : ApiControllerBase(environment)
{
    [HttpGet("catalog/random")]
    public async Task<IActionResult> RandomCart([FromQuery] int count = 3, CancellationToken ct = default)
    {
        if (!IsAvailable || !HasRole("buyer")) return StatusCode(403);
        var products = await checkout.RandomProductsAsync(count, ct);
        var ids = products.Select(p => p.Id).ToArray();
        var stock = await db.Inventories.Where(i => ids.Contains(i.ProductId ?? 0))
            .ToDictionaryAsync(i => i.ProductId!.Value, ct);
        return Ok(products.Select(p => new
        {
            productId = p.Id, title = p.Title, price = p.Price,
            quantity = Random.Shared.Next(1, Math.Min(3, stock[p.Id].Quantity!.Value) + 1),
            available = stock[p.Id].Quantity
        }));
    }

    [HttpGet("addresses")]
    public async Task<IActionResult> Addresses(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer")) return StatusCode(403);
        return Ok(await db.Addresses.Where(a => a.UserId == CurrentUserId)
            .Select(a => new { a.Id, a.FullName, a.Street, a.City, a.State, a.Country, a.IsDefault })
            .ToListAsync(ct));
    }

    [HttpPost("addresses/{id:int}/edit")]
    public async Task<IActionResult> EditAddress(int id, AddressUpdateRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer")) return StatusCode(403);
        if (new[] { request.FullName, request.Street, request.City, request.State, request.Country }
            .Any(string.IsNullOrWhiteSpace) || request.FullName.Length > 100 || request.Street.Length > 100 ||
            request.City.Length > 50 || request.State.Length > 50 || request.Country.Length > 50)
            return BadRequest(new { error = "Address fields are required and must fit the allowed lengths" });
        var address = await db.Addresses.SingleOrDefaultAsync(a => a.Id == id && a.UserId == CurrentUserId, ct);
        if (address is null) return NotFound();
        address.FullName = request.FullName.Trim();
        address.Street = request.Street.Trim();
        address.City = request.City.Trim();
        address.State = request.State.Trim();
        address.Country = request.Country.Trim();
        await db.SaveChangesAsync(ct);
        return Ok(new { address.Id, address.FullName, address.Street, address.City, address.State, address.Country, address.IsDefault });
    }
}
