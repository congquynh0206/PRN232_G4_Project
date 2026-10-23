using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;

namespace G4.Api.Controllers;

[ApiController]
[Route("api/addresses")]
public sealed class BuyerAddressesController(
    IBuyerAddressService addresses,
    IHostEnvironment environment
) : ApiControllerBase(environment)
{
    private IActionResult? Invalid(BuyerAddressInput input)
    {
        var fields = BuyerAddressRules.Validate(input);
        return fields.Count == 0
            ? null
            : BadRequest(
                new { error = "Vui lòng kiểm tra lại các trường địa chỉ được đánh dấu.", fields }
            );
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await addresses.ListAsync(CurrentUserId, ct));
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create(BuyerAddressInput input, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Invalid(input) ?? Ok(await addresses.CreateAsync(CurrentUserId, input, ct));
    }

    [HttpPost("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, BuyerAddressInput input, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Invalid(input) ?? Ok(await addresses.UpdateAsync(CurrentUserId, id, input, ct));
    }

    [HttpPost("{id:int}/default")]
    public async Task<IActionResult> MakeDefault(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await addresses.SetDefaultAsync(CurrentUserId, id, ct));
    }

    [HttpPost("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        return Ok(await addresses.DeleteAsync(CurrentUserId, id, ct));
    }
}
