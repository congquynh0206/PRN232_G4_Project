using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;

namespace G4.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class ProductFeedbackController(
    IProductFeedbackService feedback,
    IHostEnvironment environment
) : ApiControllerBase(environment)
{
    [HttpGet("seller/product-feedback/products")]
    public async Task<IActionResult> Products(
        [FromQuery] ProductFeedbackFilter filter,
        CancellationToken ct
    )
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        return Ok(await feedback.ProductsAsync(CurrentUserId, filter, ct));
    }

    [HttpGet("seller/product-feedback/products/{id:int}/reviews")]
    public async Task<IActionResult> Reviews(
        int id,
        [FromQuery] ProductFeedbackFilter filter,
        CancellationToken ct
    )
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        return Ok(await feedback.ReviewsAsync(CurrentUserId, id, filter, ct));
    }

    [HttpPost("seller/product-feedback/reviews/{id:int}/reply")]
    public async Task<IActionResult> Reply(int id, ProductReplyInput input, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller"))
            return StatusCode(403);
        return Ok(await feedback.ReplyAsync(CurrentUserId, id, input, ct));
    }

    [HttpGet("orders/{id:int}/product-reviews")]
    public async Task<IActionResult> BuyerReviews(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await feedback.BuyerOrderAsync(CurrentUserId, id, ct));
    }

    [HttpPost("orders/{id:int}/products/{productId:int}/review")]
    public async Task<IActionResult> Submit(
        int id,
        int productId,
        ProductReviewInput input,
        CancellationToken ct
    )
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await feedback.SubmitAsync(CurrentUserId, id, productId, input, ct));
    }
}
