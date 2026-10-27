using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Data;
using Q.Sock.Server.Hubs;
using Q.Sock.Shared.Models.Auth;

namespace Q.Sock.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/sessions")]
public sealed class SessionsController(
    IDbContextFactory<ChatDbContext> dbContextFactory,
    IHubContext<ChatHub> hubContext
) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeviceSessionResponse>>> GetSessions()
    {
        if (!TryGetUserId(out var userId) || !TryGetSessionId(out var currentSessionId))
            return Unauthorized();

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var sessions = await db
            .DeviceSessions.AsNoTracking()
            .Where(session =>
                session.UserId == userId
                && session.RevokedAtUtc == null
                && session.ExpiresAtUtc > now
            )
            .OrderByDescending(session => session.LastActivityAtUtc)
            .Select(session => new DeviceSessionResponse
            {
                Id = session.Id,
                DeviceName = session.DeviceName,
                CreatedAtUtc = session.CreatedAtUtc,
                LastActivityAtUtc = session.LastActivityAtUtc,
                ExpiresAtUtc = session.ExpiresAtUtc,
                IsCurrent = session.Id == currentSessionId,
            })
            .ToListAsync();

        return Ok(sessions);
    }

    [HttpDelete("{sessionId:guid}")]
    public async Task<IActionResult> RevokeSession(Guid sessionId)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var session = await db.DeviceSessions.SingleOrDefaultAsync(item =>
            item.Id == sessionId && item.UserId == userId && item.RevokedAtUtc == null
        );
        if (session is null)
            return NotFound();

        session.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await hubContext
            .Clients.Group(ChatHub.SessionGroup(session.Id))
            .SendAsync("SessionRevoked");
        return NoContent();
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private bool TryGetSessionId(out Guid sessionId) =>
        Guid.TryParse(User.FindFirstValue("sid"), out sessionId);
}
