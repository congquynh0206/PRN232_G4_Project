using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Controllers;

[ApiController]
[Route("api/seller/shipping")]
public sealed class SellerShippingSettingsController(ApplicationDbContext db, IHostEnvironment environment)
    : ApiControllerBase(environment)
{
    [HttpGet("settings")]
    public async Task<IActionResult> Settings([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        var query = db.Products.AsNoTracking().Where(p => p.SellerId == CurrentUserId && p.IsAuction != true);
        var count = await query.CountAsync(ct);
        pageSize = Math.Clamp(pageSize, 1, 50);
        page = Math.Clamp(page, 1, Math.Max(1, (count + pageSize - 1) / pageSize));
        var pickup = await db.Addresses.AsNoTracking().Where(a => a.UserId == CurrentUserId && a.IsDefault == true)
            .OrderBy(a => a.Id).Select(a => new { a.FullName, a.Street, a.City, a.State, a.Country }).FirstOrDefaultAsync(ct);
        var items = await query.OrderBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new { p.Id, p.Title, p.WeightKg }).ToListAsync(ct);
        return Ok(new { pickup, page, pageSize, totalCount = count, items });
    }

    [HttpPost("products/{id:int}/weight")]
    public async Task<IActionResult> Weight(int id, ProductWeightRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        if (request.WeightKg is < .001m or > 10000m || decimal.Round(request.WeightKg, 3) != request.WeightKg)
            return BadRequest(new { error = "Khối lượng cần từ 0,001 đến 10.000 kg, tối đa 3 chữ số thập phân." });
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id && p.SellerId == CurrentUserId, ct);
        if (product is null) return StatusCode(403);
        product.WeightKg = request.WeightKg;
        await db.SaveChangesAsync(ct);
        return Ok(new { product.Id, product.WeightKg });
    }

    [HttpPost("pickup")]
    public async Task<IActionResult> Pickup(AddressUpdateRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        if (new[] { request.FullName, request.Street, request.City, request.State, request.Country }.Any(string.IsNullOrWhiteSpace) ||
            request.FullName.Length > 100 || request.Street.Length > 100 || request.City.Length > 50 || request.State.Length > 50 || request.Country.Length > 50)
            return BadRequest(new { error = "Nhập đầy đủ địa chỉ lấy hàng; tên/đường tối đa 100 ký tự, thành phố/tỉnh/quốc gia tối đa 50 ký tự." });
        var pickup = await db.Addresses.Where(a => a.UserId == CurrentUserId && a.IsDefault == true).OrderBy(a => a.Id).FirstOrDefaultAsync(ct);
        if (pickup is null) { pickup = new Address { UserId = CurrentUserId, IsDefault = true }; db.Addresses.Add(pickup); }
        pickup.FullName = request.FullName.Trim(); pickup.Street = request.Street.Trim(); pickup.City = request.City.Trim();
        pickup.State = request.State.Trim(); pickup.Country = request.Country.Trim();
        await db.SaveChangesAsync(ct);
        return Ok(new { pickup.Id });
    }
}
