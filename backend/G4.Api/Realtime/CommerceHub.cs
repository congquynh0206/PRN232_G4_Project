using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
namespace G4.Api.Realtime;

[Authorize(Roles = "buyer,seller,admin,shipper")]
public sealed class CommerceHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var user = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(user, out var id) || id <= 0) { Context.Abort(); return; }
        await Groups.AddToGroupAsync(Context.ConnectionId, "user:" + id);
        if (Context.User!.IsInRole("admin")) await Groups.AddToGroupAsync(Context.ConnectionId, "role:admin");
        if (Context.User.IsInRole("shipper")) await Groups.AddToGroupAsync(Context.ConnectionId, "role:shipper");
        await base.OnConnectedAsync();
    }
    // Clients cannot choose groups or publish events.
}
