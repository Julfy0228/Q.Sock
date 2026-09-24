using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Q.Sock.Shared;

namespace Q.Sock.Server;

public class ChatHub : Hub
{
    private static readonly ConcurrentDictionary<string, string> Users = new();

    private static readonly List<MessageModel> MessagesHistory = [];
    private static readonly Lock LockObject = new();

    public async Task RegisterUser(string username)
    {
        Users[Context.ConnectionId] = username;

        lock (LockObject)
        {
            Clients.Caller.SendAsync("ReceiveHistory", MessagesHistory);
        }

        await Clients.All.SendAsync("UpdateUsersList", Users.Values.Distinct().ToList());

        var sysMessage = new MessageModel { User = "Система", Text = $"{username} вошел в чат" };
        await Clients.All.SendAsync("ReceiveMessage", sysMessage);
    }

    public async Task SendMessage(string messageText)
    {
        if (!Users.TryGetValue(Context.ConnectionId, out var username))
            username = "Аноним";

        var message = new MessageModel
        {
            User = username,
            Text = messageText,
            Time = DateTime.Now
        };

        lock (LockObject)
        {
            MessagesHistory.Add(message);
        }

        await Clients.All.SendAsync("ReceiveMessage", message);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Users.TryRemove(Context.ConnectionId, out var username))
        {
            await Clients.All.SendAsync("UpdateUsersList", Users.Values.Distinct().ToList());
            var sysMessage = new MessageModel { User = "Система", Text = $"{username} вышел из чата" };
            await Clients.All.SendAsync("ReceiveMessage", sysMessage);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
