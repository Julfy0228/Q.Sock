using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Data;
using Q.Sock.Server.Entities;

namespace Q.Sock.Server.Services;

public static class DatabaseBootstrap
{
    public static async Task Initialize(IServiceProvider services)
    {
        var db = services.GetRequiredService<ChatDbContext>();
        var cfg = services.GetRequiredService<IConfiguration>();
        var login = cfg["Bootstrap:AdminUser"];
        var password = cfg["Bootstrap:AdminPassword"];
        if (!string.IsNullOrWhiteSpace(login) && !string.IsNullOrWhiteSpace(password))
        {
            var manager = services.GetRequiredService<UserManager<UserEntity>>();
            var user = await manager.FindByNameAsync(login);
            if (user == null)
            {
                user = new()
                {
                    Id = Guid.NewGuid(),
                    UserName = login,
                    DisplayName = login,
                    IsAdmin = true,
                };
                var r = await manager.CreateAsync(user, password);
                if (!r.Succeeded)
                    throw new InvalidOperationException(
                        string.Join("; ", r.Errors.Select(x => x.Description))
                    );
            }
        }
        if (await db.Messages.AnyAsync(x => x.ChatId == null))
        {
            var users = await db.Users.ToArrayAsync();
            if (users.Length > 0)
            {
                var chat = new ChatEntity
                {
                    Id = Guid.NewGuid(),
                    Name = "Архив общего чата",
                    OwnerId = users[0].Id,
                    Type = "group",
                };
                db.Chats.Add(chat);
                foreach (var u in users)
                    db.ChatParticipants.Add(
                        new()
                        {
                            ChatId = chat.Id,
                            UserId = u.Id,
                            Role = u.Id == chat.OwnerId ? "owner" : "member",
                        }
                    );
                await db.SaveChangesAsync();
                await db
                    .Messages.Where(x => x.ChatId == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ChatId, (Guid?)chat.Id));
            }
        }
    }
}
