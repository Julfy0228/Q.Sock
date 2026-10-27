using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Data;
using Q.Sock.Server.Entities;
using Q.Sock.Server.Hubs;
using Q.Sock.Shared.Models;

namespace Q.Sock.Server.Services;

public sealed record Peer(string ConnectionId, Guid UserId, Guid CallId, Guid SessionId);

public sealed class CallPresence
{
    public ConcurrentDictionary<string, Peer> Peers { get; } = new();
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public ConcurrentDictionary<string, (bool Muted, bool CanShare)> Policies { get; } = new();
}

public sealed class CallService(
    ChatDbContext db,
    AccessService access,
    MessengerService messenger,
    IHubContext<ChatHub> hub,
    CallPresence presence
)
{
    public async Task<CallEntity> Get(Guid id, Guid user)
    {
        var c = await db.Calls.FindAsync(id) ?? throw new ApiException(404, "Звонок не найден.");
        await access.Chat(c.ChatId, user);
        return c;
    }

    public async Task<CallInfo> Start(Guid user, CallRequest r)
    {
        var p = await access.Chat(r.ChatId, user);
        if (!p.CanConnect)
            throw new ApiException(403, "Подключение ограничено.");
        var chat =
            await db.Chats.FindAsync(r.ChatId) ?? throw new ApiException(404, "Чат не найден.");
        var c = await db.Calls.SingleOrDefaultAsync(x =>
            x.ChatId == r.ChatId && x.Status == "live"
        );
        if (c == null)
        {
            if (chat.Managed)
                await access.ManageChat(chat.Id, user);
            c = new()
            {
                Id = Guid.NewGuid(),
                ChatId = r.ChatId,
                OrganizerId = user,
                Title = chat.Name,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.Calls.Add(c);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                c = await db.Calls.SingleAsync(x => x.ChatId == r.ChatId && x.Status == "live");
            }
        }
        var ring = await db
            .ChatParticipants.Where(x =>
                x.ChatId == r.ChatId && x.Active && x.CanConnect && x.UserId != user
            )
            .Select(x => x.UserId)
            .ToArrayAsync();
        if (chat.Type == "room")
            ring = [];
        else if (chat.Type != "direct")
            ring = ring.Where(x =>
                    !r.ExcludedUserIds.Contains(x) && (r.RingAll || r.RingUserIds.Contains(x))
                )
                .ToArray();
        await hub.Clients.Users(ring.Select(x => x.ToString())).SendAsync("IncomingCall", Dto(c));
        await messenger.NotifyChat(r.ChatId, "call", Dto(c));
        return Dto(c);
    }

    public async Task<object> Join(Guid callId, Guid user, string connectionId, Guid sessionId)
    {
        var c = await Get(callId, user);
        var p = await access.Chat(c.ChatId, user);
        if (!p.CanConnect)
            throw new ApiException(403, "Подключение запрещено модератором.");
        if (c.Status == "ended")
            throw new ApiException(409, "Звонок завершён.");
        if (c.Status == "planned")
        {
            await access.ManageChat(c.ChatId, user);
            c.Status = "live";
        }
        var cp = await db.CallParticipants.FindAsync(callId, user);
        if (cp?.Removed == true)
            throw new ApiException(403, "Вы исключены из этого звонка.");
        if (cp == null)
        {
            cp = new()
            {
                CallId = callId,
                UserId = user,
                CanShare = p.CanShare,
            };
            db.CallParticipants.Add(cp);
        }
        await db.SaveChangesAsync();
        await presence.Gate.WaitAsync();
        try
        {
            if (
                presence.Peers.Values.Count(x => x.CallId == callId) >= 12
                && !presence.Peers.ContainsKey(connectionId)
            )
                throw new ApiException(
                    409,
                    "В этой конфигурации поддерживается до 12 подключений к звонку."
                );
            if (presence.Peers.TryGetValue(connectionId, out var old) && old.CallId != callId)
                await Leave(connectionId);
            var peers = presence
                .Peers.Values.Where(x => x.CallId == callId && x.ConnectionId != connectionId)
                .ToArray();
            presence.Peers[connectionId] = new(connectionId, user, callId, sessionId);
            presence.Policies[connectionId] = (cp.Muted || !p.CanSpeak, cp.CanShare && p.CanShare);
            await hub
                .Clients.Clients(peers.Select(x => x.ConnectionId))
                .SendAsync(
                    "PeerJoined",
                    new
                    {
                        connectionId,
                        userId = user,
                        callId,
                    }
                );
            return new
            {
                peers = peers
                    .Select(peer => new
                    {
                        peer.ConnectionId,
                        peer.UserId,
                        peer.CallId,
                    })
                    .ToArray(),
                muted = cp.Muted || !p.CanSpeak,
                canShare = cp.CanShare && p.CanShare,
            };
        }
        finally
        {
            presence.Gate.Release();
        }
    }

    public async Task Signal(Guid user, string sender, string target, string type, JsonElement data)
    {
        if (type is not ("description" or "candidate") || data.GetRawText().Length > 100000)
            throw new ApiException(400, "Некорректный сигнал.");
        if (
            !presence.Peers.TryGetValue(sender, out var a)
            || !presence.Peers.TryGetValue(target, out var b)
            || a.UserId != user
            || a.CallId != b.CallId
        )
            throw new ApiException(403, "Участники не находятся в одном звонке.");
        var c = await Get(a.CallId, user);
        var targetAccess = await access.Chat(c.ChatId, b.UserId);
        var actor = await access.Chat(c.ChatId, user);
        if (
            c.Status != "live"
            || !actor.CanConnect
            || !targetAccess.CanConnect
            || await db.CallParticipants.AnyAsync(x =>
                x.CallId == c.Id && (x.UserId == user || x.UserId == b.UserId) && x.Removed
            )
        )
            throw new ApiException(403, "Звонок недоступен.");
        await hub
            .Clients.Client(target)
            .SendAsync(
                "RtcSignal",
                new
                {
                    from = sender,
                    type,
                    data,
                    callId = a.CallId,
                    userId = user,
                }
            );
    }

    public async Task Leave(string connectionId)
    {
        presence.Policies.TryRemove(connectionId, out _);
        if (presence.Peers.TryRemove(connectionId, out var p))
            await hub
                .Clients.Clients(
                    presence
                        .Peers.Values.Where(x => x.CallId == p.CallId)
                        .Select(x => x.ConnectionId)
                )
                .SendAsync("PeerLeft", new { connectionId, callId = p.CallId });
    }

    public async Task Moderate(Guid callId, Guid actor, Guid target, string action)
    {
        var c = await Get(callId, actor);
        if (await db.Chats.AnyAsync(chat => chat.Id == c.ChatId && chat.Type == "direct"))
            throw new ApiException(400, "В личном звонке нет модераторских ограничений.");
        await access.ManageChat(c.ChatId, actor);
        var cp =
            await db.CallParticipants.FindAsync(callId, target)
            ?? throw new ApiException(404, "Участник ещё не подключался.");
        var permission = await access.Chat(c.ChatId, target);
        if (
            action == "allow-mic" && !permission.CanSpeak
            || action == "allow-share" && !permission.CanShare
        )
            throw new ApiException(400, "Сначала снимите ограничение в настройках участника чата.");
        if (action == "mute")
            cp.Muted = true;
        else if (action == "allow-mic")
            cp.Muted = false;
        else if (action == "remove")
            cp.Removed = true;
        else if (action == "deny-share")
            cp.CanShare = false;
        else if (action == "allow-share")
            cp.CanShare = true;
        else
            throw new ApiException(400, "Неизвестное действие.");
        access.Audit(actor, "call." + action, $"{callId}/{target}");
        await db.SaveChangesAsync();
        await hub
            .Clients.User(target.ToString())
            .SendAsync("CallModerated", new { callId, action });
        if (action == "remove")
            foreach (
                var p in presence
                    .Peers.Values.Where(x => x.CallId == callId && x.UserId == target)
                    .ToArray()
            )
                await Leave(p.ConnectionId);
    }

    public async Task End(Guid callId, Guid user)
    {
        var c = await Get(callId, user);
        if (!await db.Chats.AnyAsync(chat => chat.Id == c.ChatId && chat.Type == "direct"))
            await access.ManageChat(c.ChatId, user);
        c.Status = "ended";
        await db.SaveChangesAsync();
        var peers = presence.Peers.Values.Where(x => x.CallId == callId).ToArray();
        await hub
            .Clients.Clients(peers.Select(x => x.ConnectionId))
            .SendAsync("CallEnded", new { callId });
        foreach (var p in peers)
            await Leave(p.ConnectionId);
        await messenger.NotifyChat(c.ChatId, "call");
    }

    public static CallInfo Dto(CallEntity c) =>
        new()
        {
            Id = c.Id,
            ChatId = c.ChatId,
            OrganizerId = c.OrganizerId,
            Title = c.Title,
            Status = c.Status,
            StartsAtUtc = c.StartsAtUtc,
            DurationMinutes = c.DurationMinutes,
            Reminder = c.Reminder,
            ContactEmail = c.ContactEmail,
            Materials = JsonSerializer.Deserialize<string[]>(c.MaterialsJson) ?? [],
        };
}
