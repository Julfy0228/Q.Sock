using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Data;
using Q.Sock.Server.Services;
using Q.Sock.Shared.Models;

namespace Q.Sock.Server.Controllers;

[ApiController, Authorize, Route("api/calls")]
public sealed class CallsController(
    ChatDbContext db,
    CallService calls,
    IConfiguration config,
    CallPresence presence
) : ControllerBase
{
    [HttpGet]
    public async Task<object> List()
    {
        var uid = User.UserId();
        return (
            await db
                .Calls.Where(x =>
                    x.Status != "ended"
                    && db.ChatParticipants.Any(p =>
                        p.ChatId == x.ChatId && p.UserId == uid && p.Active
                    )
                )
                .OrderByDescending(x => x.CreatedAtUtc)
                .ToArrayAsync()
        ).Select(CallService.Dto);
    }

    [HttpGet("{id:guid}")]
    public async Task<CallInfo> Get(Guid id) => CallService.Dto(await calls.Get(id, User.UserId()));

    [HttpPost]
    public Task<CallInfo> Start(CallRequest r) => calls.Start(User.UserId(), r);

    [HttpGet("{id:guid}/members")]
    public async Task<object> Members(Guid id)
    {
        await calls.Get(id, User.UserId());
        var ids = presence
            .Peers.Values.Where(peer => peer.CallId == id)
            .Select(peer => peer.UserId)
            .Distinct()
            .ToArray();
        return await db
            .CallParticipants.Where(member => member.CallId == id && ids.Contains(member.UserId))
            .Join(
                db.Users,
                member => member.UserId,
                user => user.Id,
                (member, user) =>
                    new
                    {
                        member.UserId,
                        name = user.DisplayName == "" ? user.UserName! : user.DisplayName,
                        member.Muted,
                        member.CanShare,
                    }
            )
            .ToArrayAsync();
    }

    [HttpPost("{id:guid}/end")]
    public async Task<IActionResult> End(Guid id)
    {
        await calls.End(id, User.UserId());
        return NoContent();
    }

    [HttpPost("{id:guid}/members/{target:guid}/{operation}")]
    public async Task<IActionResult> Moderate(Guid id, Guid target, string operation)
    {
        await calls.Moderate(id, User.UserId(), target, operation);
        return NoContent();
    }

    [HttpGet("ice")]
    public object Ice() =>
        new { iceServers = config.GetSection("WebRtc:IceServers").Get<IceServerOptions[]>() ?? [] };
}

public sealed class IceServerOptions
{
    public string[] Urls { get; set; } = [];
    public string? Username { get; set; }
    public string? Credential { get; set; }
}
