using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Data;
using Q.Sock.Server.Entities;
using Q.Sock.Shared.Models;

namespace Q.Sock.Server.Hubs;

[Authorize]
public sealed class ChatHub(IDbContextFactory<ChatDbContext> dbContextFactory) : Hub
{
    private static readonly Dictionary<string, Guid> ConnectedUsers = [];
    private static readonly Lock UsersLock = new();

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == userId)
            ?? throw new HubException("Пользователь не найден.");
        var username = user.UserName ?? "Пользователь";
        bool wasOnline;

        lock (UsersLock)
        {
            wasOnline = ConnectedUsers.ContainsValue(user.Id);
            ConnectedUsers[Context.ConnectionId] = user.Id;
        }

        var history = await db.Messages
            .AsNoTracking()
            .Join(db.Users, message => message.SenderId, sender => sender.Id,
                (message, sender) => new Message
                {
                    Id = message.Id,
                    SenderName = sender.UserName ?? "Неизвестный пользователь",
                    Text = message.Text,
                    SentAt = message.SentAt
                })
            .ToListAsync();

        await Clients.Caller.SendAsync("ReceiveHistory", history.OrderBy(message => message.SentAt));
        await BroadcastUsersAsync();

        if (!wasOnline)
        {
            await Clients.All.SendAsync("ReceiveMessage", new Message
            {
                Id = Guid.NewGuid(),
                SenderName = "Система",
                Text = $"{username} вошел в чат",
                SentAt = DateTimeOffset.Now
            });
        }

        await base.OnConnectedAsync();
    }

    public async Task SendMessage(string messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
            return;

        var senderId = GetUserId();

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var entity = new MessageEntity
        {
            Id = Guid.NewGuid(), SenderId = senderId,
            Text = messageText.Trim(), SentAt = DateTimeOffset.Now
        };
        db.Messages.Add(entity);
        await db.SaveChangesAsync();

        var senderName = await db.Users.Where(user => user.Id == senderId)
            .Select(user => user.UserName).SingleOrDefaultAsync()
            ?? throw new HubException("Пользователь не найден.");

        await Clients.All.SendAsync("ReceiveMessage", new Message
        {
            Id = entity.Id, SenderName = senderName,
            Text = entity.Text, SentAt = entity.SentAt
        });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Guid? userId = null;
        var userStillOnline = false;
        lock (UsersLock)
        {
            if (ConnectedUsers.Remove(Context.ConnectionId, out var removedId))
            {
                userId = removedId;
                userStillOnline = ConnectedUsers.ContainsValue(removedId);
            }
        }

        if (userId.HasValue)
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var username = await db.Users.Where(user => user.Id == userId.Value)
                .Select(user => user.UserName).SingleOrDefaultAsync();
            await BroadcastUsersAsync();
            if (!userStillOnline && username is not null)
                await Clients.All.SendAsync("ReceiveMessage", new Message
                {
                    Id = Guid.NewGuid(), SenderName = "Система",
                    Text = $"{username} вышел из чата", SentAt = DateTimeOffset.Now
                });
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task BroadcastUsersAsync()
    {
        Guid[] ids;
        lock (UsersLock)
            ids = [.. ConnectedUsers.Values.Distinct()];

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var users = await db.Users.AsNoTracking().Where(user => ids.Contains(user.Id))
            .OrderBy(user => user.UserName)
            .Select(user => new User
            {
                Id = user.Id,
                UserName = user.UserName ?? "Неизвестный пользователь"
            })
            .ToListAsync();
        await Clients.All.SendAsync("UpdateUsersList", users);
    }

    private Guid GetUserId()
    {
        var value = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new HubException("Пользователь не авторизован.");
    }
}
