using Microsoft.AspNetCore.Mvc;

namespace G4.Api.Controllers;

/// <summary>
/// Region lists for address forms and VietMap house-number suggestions for Vietnam.
/// Suggestions only fill the form; the address book and address-change endpoints still
/// validate and canonicalize every field on the server.
/// </summary>
[ApiController]
[Route("api/address-suggestions")]
public sealed class AddressSuggestionsController(
    IAddressLookupGateway lookup,
    IHostEnvironment environment
) : ApiControllerBase(environment)
{
    private const int MinText = 3;
    private const int MaxText = 100;
    private const int MaxRef = 512;

    [HttpGet("regions")]
    public IActionResult Regions()
    {
        if (!IsAvailable || !HasAnyRole())
            return StatusCode(403);
        return Ok(
            new
            {
                lookupEnabled = HasRole("buyer") && lookup.Enabled,
                countries = new[]
                {
                    new
                    {
                        value = AddressRegions.Vietnam,
                        label = "Việt Nam",
                        dialCode = DeliveryAddressRules.DialCode(AddressRegions.Vietnam),
                        states = AddressRegions
                            .VietnamProvinces.Select(p => new { value = p.Name, label = p.Name })
                            .ToArray(),
                    },
                    new
                    {
                        value = AddressRegions.UnitedStates,
                        label = "Hoa Kỳ",
                        dialCode = DeliveryAddressRules.DialCode(AddressRegions.UnitedStates),
                        states = AddressRegions
                            .UsStates.Select(s => new
                            {
                                value = s.Code,
                                label = $"{s.Name} ({s.Code})",
                            })
                            .ToArray(),
                    },
                },
            }
        );
    }

    [HttpGet]
    public async Task<IActionResult> Suggest([FromQuery] string? q, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        var text = DeliveryAddressRules.Clean(q);
        if (text.Length < MinText || text.Length > MaxText)
            return BadRequest(
                new { error = $"Nhập {MinText}–{MaxText} ký tự để tìm địa chỉ." }
            );
        return await Guard(async () => Ok(await lookup.SuggestAsync(text, ct)));
    }

    [HttpGet("detail")]
    public async Task<IActionResult> Detail([FromQuery] string? @ref, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer"))
            return StatusCode(403);
        if (
            string.IsNullOrWhiteSpace(@ref)
            || @ref.Length > MaxRef
            || !(@ref.StartsWith("auto:") || @ref.StartsWith("geocode:"))
        )
            return BadRequest(new { error = "Gợi ý địa chỉ không hợp lệ." });
        return await Guard(async () =>
        {
            var detail = await lookup.DetailAsync(@ref, ct);
            return detail is null
                ? NotFound(new { error = "Không tìm thấy địa chỉ." })
                : Ok(detail);
        });
    }

    private async Task<IActionResult> Guard(Func<Task<IActionResult>> work)
    {
        if (!lookup.Enabled)
            return StatusCode(
                503,
                new { error = "Chưa bật gợi ý địa chỉ.", code = "LookupDisabled" }
            );
        try
        {
            return await work();
        }
        catch (AddressLookupUnavailableException ex)
        {
            return StatusCode(503, new { error = ex.Message, code = "LookupUnavailable" });
        }
    }
}
