using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace G4.Api.Controllers;

[ApiController]
[Route("api/best-offers")]
public sealed class BestOffersController(
    ApplicationDbContext db,
    IBestOfferService offers,
    IHostEnvironment environment
) : ApiControllerBase(environment)
{
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await offers.GetOfferableProductsAsync(ct));
    }

    [HttpGet("buyer")]
    public async Task<IActionResult> BuyerOffers(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await offers.GetBuyerOffersAsync(CurrentUserId, ct));
    }

    [HttpGet("seller")]
    public async Task<IActionResult> SellerOffers(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        return Ok(await offers.GetSellerOffersAsync(CurrentUserId, ct));
    }

    [HttpGet("seller/products")]
    public async Task<IActionResult> SellerProducts(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        return Ok(await db.Products.AsNoTracking()
            .Where(x => x.SellerId == CurrentUserId && x.IsAuction != true)
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                productId = x.Id,
                title = x.Title,
                price = x.Price,
                allowOffers = x.AllowOffers,
                autoAcceptPrice = x.AutoAcceptOfferPrice,
                autoDeclineBelow = x.AutoDeclineOfferBelow,
            }).ToListAsync(ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(BestOfferCreateInput input, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await offers.CreateAsync(CurrentUserId, input, ct));
    }

    [HttpPost("{id:long}/seller/accept")]
    public async Task<IActionResult> SellerAccept(long id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        return Ok(await offers.SellerAcceptAsync(CurrentUserId, id, ct));
    }

    [HttpPost("{id:long}/seller/decline")]
    public async Task<IActionResult> SellerDecline(long id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        return Ok(await offers.SellerDeclineAsync(CurrentUserId, id, ct));
    }

    [HttpPost("{id:long}/seller/counter")]
    public async Task<IActionResult> SellerCounter(
        long id,
        BestOfferActionInput input,
        CancellationToken ct
    )
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        return Ok(await offers.SellerCounterAsync(CurrentUserId, id, input, ct));
    }

    [HttpPost("{id:long}/buyer/accept")]
    public async Task<IActionResult> BuyerAccept(long id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await offers.BuyerAcceptAsync(CurrentUserId, id, ct));
    }

    [HttpPost("{id:long}/buyer/payment-profile")]
    public async Task<IActionResult> UpdatePaymentProfile(
        long id,
        BestOfferActionInput input,
        CancellationToken ct
    )
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await offers.UpdatePaymentProfileAsync(CurrentUserId, id, input, ct));
    }

    [HttpPost("{id:long}/buyer/decline")]
    public async Task<IActionResult> BuyerDecline(long id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await offers.BuyerDeclineAsync(CurrentUserId, id, ct));
    }

    [HttpPost("{id:long}/buyer/counter")]
    public async Task<IActionResult> BuyerCounter(
        long id,
        BestOfferActionInput input,
        CancellationToken ct
    )
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await offers.BuyerCounterAsync(CurrentUserId, id, input, ct));
    }

    [HttpPost("seller/settings")]
    public async Task<IActionResult> Settings(BestOfferSettingsInput input, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        await offers.UpdateProductSettingsAsync(CurrentUserId, input, ct);
        return NoContent();
    }
}
