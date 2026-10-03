using G4.Contracts.Checkout;
using Microsoft.AspNetCore.Mvc;

namespace G4.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class DisputesController(IDisputeService disputes, IHostEnvironment environment) : ApiControllerBase(environment)
{
    private string ActorRole => HasRole("buyer") ? "buyer" : HasRole("seller") ? "seller" : "admin";
    private bool CanRead => IsAvailable && (HasRole("buyer") || HasRole("seller") || HasRole("admin"));

    [HttpGet("disputes/page")]
    public async Task<IActionResult> Page([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery] string filter = "open", CancellationToken ct = default)
    {
        if (!CanRead) return StatusCode(403);
        return Ok(await disputes.GetPageAsync(CurrentUserId, ActorRole, page, pageSize, filter, ct));
    }

    [HttpGet("disputes/{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        if (!CanRead) return StatusCode(403);
        return Ok(await disputes.GetDetailAsync(id, CurrentUserId, ActorRole, ct));
    }

    [HttpPost("orders/{id:int}/disputes")]
    public async Task<IActionResult> Open(int id, OpenDisputeRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer")) return StatusCode(403);
        var result = await disputes.OpenAsync(id, CurrentUserId, request, ct);
        return Ok(new { result.Id, result.Status });
    }

    [HttpPost("disputes/{id:int}/evidence")]
    public async Task<IActionResult> Evidence(int id, DisputeEvidenceRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !(HasRole("buyer") || HasRole("seller"))) return StatusCode(403);
        await disputes.AddEvidenceAsync(id, CurrentUserId, ActorRole, request, ct);
        return Ok(new { saved = true });
    }

    [HttpPost("disputes/{id:int}/proposal")]
    public async Task<IActionResult> Proposal(int id, DisputeProposalRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("seller")) return StatusCode(403);
        await disputes.ProposeAsync(id, CurrentUserId, request, ct);
        return Ok(new { saved = true });
    }

    [HttpPost("disputes/{id:int}/response")]
    public async Task<IActionResult> SubmitResponse(int id, DisputeResponseRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("buyer")) return StatusCode(403);
        await disputes.RespondAsync(id, CurrentUserId, request, ct);
        return Ok(new { saved = true });
    }

    [HttpPost("disputes/{id:int}/resolution")]
    public async Task<IActionResult> Resolution(int id, DisputeResolutionRequest request, CancellationToken ct)
    {
        if (!IsAvailable || !HasRole("admin")) return StatusCode(403);
        await disputes.ResolveAsync(id, CurrentUserId, request, ct);
        return Ok(new { saved = true });
    }
}
