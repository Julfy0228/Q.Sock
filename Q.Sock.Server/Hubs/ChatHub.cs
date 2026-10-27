using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Data;
using Q.Sock.Server.Services;
using Q.Sock.Shared.Models;

namespace Q.Sock.Server.Hubs;

[Authorize]
public sealed class ChatHub(CallService calls, MessengerService messenger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirst("sid")?.Value, out var sid))
            await Groups.AddToGroupAsync(Context.ConnectionId, SessionGroup(sid));
        await base.OnConnectedAsync();
    }

    public Task<object> JoinCall(Guid callId) =>
        calls.Join(
            callId,
            Context.User!.UserId(),
            Context.ConnectionId,
            Guid.Parse(Context.User!.FindFirst("sid")!.Value)
        );

    public Task LeaveCall() => calls.Leave(Context.ConnectionId);

    public Task Signal(string target, string type, JsonElement data) =>
        calls.Signal(Context.User!.UserId(), Context.ConnectionId, target, type, data);

    public Task<Guid> SendChatMessage(Guid chatId, SendMessageRequest request) =>
        messenger.Send(chatId, Context.User!.UserId(), request);

    public override async Task OnDisconnectedAsync(Exception? e)
    {
        await calls.Leave(Context.ConnectionId);
        await base.OnDisconnectedAsync(e);
    }

    public static string SessionGroup(Guid id) => $"session:{id}";
}

public sealed class ActiveSessionHubFilter(IDbContextFactory<ChatDbContext> factory) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next
    )
    {
        await using var db = await factory.CreateDbContextAsync();
        var uid = context.Context.User!.UserId();
        if (
            !Guid.TryParse(context.Context.User?.FindFirst("sid")?.Value, out var sid)
            || !await db.DeviceSessions.AnyAsync(x =>
                x.Id == sid
                && x.UserId == uid
                && x.RevokedAtUtc == null
                && x.ExpiresAtUtc > DateTime.UtcNow
            )
            || !await db.Users.AnyAsync(x => x.Id == uid && !x.IsSuspended)
        )
        {
            context.Context.Abort();
            throw new HubException("Сессия завершена.");
        }
        try
        {
            return await next(context);
        }
        catch (ApiException e)
        {
            throw new HubException(e.Message);
        }
    }
}
