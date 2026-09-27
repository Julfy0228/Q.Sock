using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Data;
using Q.Sock.Server.Entities;
using Q.Sock.Shared.Models;

namespace Q.Sock.Server;

public class ChatHub(IDbContextFactory<ChatDbContext> dbContextFactory) : Hub
{
    private static readonly Dictionary<string, Guid> ConnectedUsers = [];
    private static readonly Lock UsersLock = new();

    public async Task RegisterUser(string username)
    {
        username = username.Trim();
        if (string.IsNullOrWhiteSpace(username))
            throw new HubException("Имя пользователя не может быть пустым.");

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var user = await db.Users.SingleOrDefaultAsync(item => item.Name == username);
        if (user is null)
        {
            user = new UserEntity { Id = Guid.NewGuid(), Name = username };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        lock (UsersLock)
        {
            ConnectedUsers[Context.ConnectionId] = user.Id;
        }

        var history = await db.Messages
            .AsNoTracking()
            .Join(db.Users, message => message.SenderId, sender => sender.Id,
                (message, sender) => new Message
                {
                    Id = message.Id,
                    SenderName = sender.Name,
                    Text = message.Text,
                    SentAt = message.SentAt
                })
            .ToListAsync();

            history = history.OrderBy(message => message.SentAt).ToList();

        await Clients.Caller.SendAsync("ReceiveHistory", history);
        await BroadcastUsersAsync();

        await Clients.All.SendAsync("ReceiveMessage", new Message
        {
            Id = Guid.NewGuid(), SenderName = "Система",
            Text = $"{username} вошел в чат", SentAt = DateTimeOffset.Now
        });
    }

    public async Task SendMessage(string messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
            return;

        Guid senderId;
        lock (UsersLock)
        {
            if (!ConnectedUsers.TryGetValue(Context.ConnectionId, out senderId))
                return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var entity = new MessageEntity
        {
            Id = Guid.NewGuid(), SenderId = senderId,
            Text = messageText.Trim(), SentAt = DateTimeOffset.Now
        };
        db.Messages.Add(entity);
        await db.SaveChangesAsync();

        var senderName = await db.Users.Where(user => user.Id == senderId)
            .Select(user => user.Name).SingleAsync();

        await Clients.All.SendAsync("ReceiveMessage", new Message
        {
            Id = entity.Id, SenderName = senderName,
            Text = entity.Text, SentAt = entity.SentAt
        });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Guid? userId = null;
        lock (UsersLock)
        {
            if (ConnectedUsers.Remove(Context.ConnectionId, out var removedId))
                userId = removedId;
        }

        if (userId.HasValue)
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var username = await db.Users.Where(user => user.Id == userId.Value)
                .Select(user => user.Name).SingleOrDefaultAsync();
            await BroadcastUsersAsync();
            if (username is not null)
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
            ids = ConnectedUsers.Values.Distinct().ToArray();

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var users = await db.Users.AsNoTracking().Where(user => ids.Contains(user.Id))
            .OrderBy(user => user.Name)
            .Select(user => new User { Id = user.Id, Name = user.Name })
            .ToListAsync();
        await Clients.All.SendAsync("UpdateUsersList", users);
    }
}
