using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Data;
using Q.Sock.Server.Hubs;

namespace Q.Sock.Server.Services;

public sealed class CallPolicyReconciler(
    IServiceScopeFactory scopes,
    CallPresence presence,
    ILogger<CallPolicyReconciler> log
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(token))
        {
            try
            {
                await Reconcile(token);
            }
            catch (Exception exception) when (!token.IsCancellationRequested)
            {
                log.LogWarning(exception, "Не удалось обновить доступ участников звонков.");
            }
        }
    }

    private async Task Reconcile(CancellationToken token)
    {
        if (presence.Peers.IsEmpty)
            return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        var calls = scope.ServiceProvider.GetRequiredService<CallService>();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ChatHub>>();
        foreach (var peer in presence.Peers.Values.ToArray())
        {
            var call = await db
                .Calls.AsNoTracking()
                .SingleOrDefaultAsync(call => call.Id == peer.CallId, token);
            var permission = call is null
                ? null
                : await db
                    .ChatParticipants.AsNoTracking()
                    .SingleOrDefaultAsync(
                        member => member.ChatId == call.ChatId && member.UserId == peer.UserId,
                        token
                    );
            var participant = await db
                .CallParticipants.AsNoTracking()
                .SingleOrDefaultAsync(
                    member => member.CallId == peer.CallId && member.UserId == peer.UserId,
                    token
                );
            var active =
                call?.Status == "live"
                && permission is { Active: true, CanConnect: true }
                && participant is { Removed: false }
                && await db.Users.AnyAsync(
                    user => user.Id == peer.UserId && !user.IsSuspended,
                    token
                )
                && await db.DeviceSessions.AnyAsync(
                    session =>
                        session.Id == peer.SessionId
                        && session.UserId == peer.UserId
                        && session.RevokedAtUtc == null
                        && session.ExpiresAtUtc > DateTime.UtcNow,
                    token
                );
            if (!active)
            {
                await hub
                    .Clients.Client(peer.ConnectionId)
                    .SendAsync(
                        "CallModerated",
                        new { callId = peer.CallId, action = "remove" },
                        token
                    );
                await calls.Leave(peer.ConnectionId);
                continue;
            }
            var policy = (
                Muted: participant!.Muted || !permission!.CanSpeak,
                CanShare: participant!.CanShare && permission!.CanShare
            );
            var previous = presence.Policies.GetValueOrDefault(peer.ConnectionId);
            if (policy.Muted != previous.Muted)
                await hub
                    .Clients.Client(peer.ConnectionId)
                    .SendAsync(
                        "CallModerated",
                        new { callId = peer.CallId, action = policy.Muted ? "mute" : "allow-mic" },
                        token
                    );
            if (policy.CanShare != previous.CanShare)
                await hub
                    .Clients.Client(peer.ConnectionId)
                    .SendAsync(
                        "CallModerated",
                        new
                        {
                            callId = peer.CallId,
                            action = policy.CanShare ? "allow-share" : "deny-share",
                        },
                        token
                    );
            if (presence.Peers.ContainsKey(peer.ConnectionId))
                presence.Policies[peer.ConnectionId] = policy;
        }
    }
}
