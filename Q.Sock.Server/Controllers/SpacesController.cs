using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Data;
using Q.Sock.Server.Entities;
using Q.Sock.Server.Services;
using Q.Sock.Shared.Models;

namespace Q.Sock.Server.Controllers;

[ApiController, Authorize, Route("api")]
public sealed class SpacesController(
    ChatDbContext db,
    AccessService access,
    MessengerService messenger
) : ControllerBase
{
    private Guid Me => User.UserId();

    [HttpGet("guilds")]
    public async Task<object> Guilds() =>
        await db
            .Guilds.Join(
                db.GuildMembers.Where(x => x.UserId == Me && x.Active),
                g => g.Id,
                m => m.GuildId,
                (g, m) =>
                    new GuildInfo
                    {
                        Id = g.Id,
                        Name = g.Name,
                        Managed = g.Managed,
                        CanManage =
                            m.Role == "owner" || m.Role == "moderator" || m.Role == "teacher",
                    }
            )
            .ToArrayAsync();

    [HttpPost("guilds")]
    public async Task<object> Create(NamedRequest r)
    {
        var u = await access.User(Me);
        if (u.IsStudent && !(await access.Policy()).StudentsCanCreateGuilds)
            throw new ApiException(403, "Создание пространств студентами отключено.");
        var g = new GuildEntity
        {
            Id = Guid.NewGuid(),
            OwnerId = Me,
            Name = MessengerController.ValidName(r.Name),
        };
        db.Guilds.Add(g);
        db.GuildMembers.Add(
            new()
            {
                GuildId = g.Id,
                UserId = Me,
                Role = "owner",
            }
        );
        var c = new ChatEntity
        {
            Id = Guid.NewGuid(),
            GuildId = g.Id,
            OwnerId = Me,
            Name = "Общий",
            Type = "channel",
        };
        db.Chats.Add(c);
        db.ChatParticipants.Add(
            new()
            {
                ChatId = c.Id,
                UserId = Me,
                Role = "owner",
            }
        );
        await db.SaveChangesAsync();
        return new { id = g.Id };
    }

    [HttpPatch("guilds/{id:guid}")]
    public async Task<IActionResult> Rename(Guid id, NamedRequest r)
    {
        var g = await access.ManageGuild(id, Me);
        if (g.Managed)
            throw new ApiException(403, "Название синхронизируется с Moodle.");
        g.Name = MessengerController.ValidName(r.Name);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("guilds/{id:guid}/members")]
    public async Task<object> Members(Guid id)
    {
        if (!await db.GuildMembers.AnyAsync(x => x.GuildId == id && x.UserId == Me && x.Active))
            throw new ApiException(403, "Нет доступа.");
        return await db
            .GuildMembers.Where(x => x.GuildId == id && x.Active)
            .Join(
                db.Users,
                m => m.UserId,
                u => u.Id,
                (m, u) =>
                    new
                    {
                        userId = u.Id,
                        u.UserName,
                        u.DisplayName,
                        m.Role,
                        m.GroupsJson,
                    }
            )
            .ToArrayAsync();
    }

    [HttpPut("guilds/{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> Add(Guid id, Guid userId, MemberUpdate r)
    {
        var g = await access.ManageGuild(id, Me);
        if (g.Managed)
            throw new ApiException(403, "Роли и состав курса синхронизируются из Moodle.");
        if (r.Role is not ("member" or "moderator"))
            throw new ApiException(400, "Некорректная роль.");
        if (g.OwnerId != Me && !(await access.User(Me)).IsAdmin)
            throw new ApiException(403, "Участников добавляет владелец.");
        await access.User(userId);
        var m = await db.GuildMembers.FindAsync(id, userId);
        if (m?.Role == "owner")
            throw new ApiException(403, "Владельца нельзя изменить.");
        if (m == null)
        {
            m = new() { GuildId = id, UserId = userId };
            db.GuildMembers.Add(m);
        }
        m.Active = true;
        m.Role = r.Role;
        var chats = await db.Chats.Where(x => x.GuildId == id).ToArrayAsync();
        foreach (var c in chats)
        {
            var p = await db.ChatParticipants.FindAsync(c.Id, userId);
            if (p == null)
            {
                p = new() { ChatId = c.Id, UserId = userId };
                db.ChatParticipants.Add(p);
            }
            p.Active = true;
            p.Role = r.Role;
        }
        await db.SaveChangesAsync();
        foreach (var c in chats)
            await messenger.NotifyChat(c.Id, "members");
        return NoContent();
    }

    [HttpDelete("guilds/{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> Remove(Guid id, Guid userId)
    {
        var g = await access.ManageGuild(id, Me);
        if (g.Managed || g.OwnerId == userId)
            throw new ApiException(403, "Нельзя исключить этого участника.");
        if (g.OwnerId != Me && !(await access.User(Me)).IsAdmin)
            throw new ApiException(403, "Только владелец.");
        var m = await db.GuildMembers.FindAsync(id, userId);
        if (m != null)
            m.Active = false;
        var ids = await db.Chats.Where(x => x.GuildId == id).Select(x => x.Id).ToArrayAsync();
        await db
            .ChatParticipants.Where(x => ids.Contains(x.ChatId) && x.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Active, false));
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("guilds/{id:guid}")]
    public async Task<object> Delete(Guid id)
    {
        var g = await access.ManageGuild(id, Me);
        var u = await access.User(Me);
        if (g.Managed)
            throw new ApiException(403, "Учебное пространство удалить нельзя.");
        if (g.OwnerId != Me && !u.IsAdmin)
            throw new ApiException(403, "Удаление доступно владельцу.");
        if (u.IsStudent && (await access.Policy()).ConfirmStudentGuildDeletion && !u.IsAdmin)
        {
            g.DeletionRequested = true;
            await db.SaveChangesAsync();
            return new { pending = true };
        }
        db.Guilds.Remove(g);
        access.Audit(Me, "guild.delete", id);
        await db.SaveChangesAsync();
        return new { pending = false };
    }

    [HttpGet("friends")]
    public async Task<object> Friends()
    {
        var rows = await db.Friendships.Where(x => x.FromId == Me || x.ToId == Me).ToArrayAsync();
        var ids = rows.Select(x => x.FromId == Me ? x.ToId : x.FromId).ToArray();
        var users = await db
            .Users.Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.UserName!);
        return rows.Select(x => new FriendInfo
        {
            UserId = x.FromId == Me ? x.ToId : x.FromId,
            UserName = users[x.FromId == Me ? x.ToId : x.FromId],
            Status = x.Status,
            Incoming = x.ToId == Me,
        });
    }

    [HttpPost("friends/{id:guid}")]
    public async Task<IActionResult> FriendRequest(Guid id)
    {
        if (id == Me)
            throw new ApiException(400, "Выберите другого пользователя.");
        await access.User(id);
        var pair = AccessService.Pair(Me, id);
        var f = await db.Friendships.SingleOrDefaultAsync(x => x.PairKey == pair);
        if (f == null)
            db.Friendships.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    FromId = Me,
                    ToId = id,
                    PairKey = pair,
                }
            );
        else if (f.ToId == Me)
            f.Status = "accepted";
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("friends/{id:guid}")]
    public async Task<IActionResult> Unfriend(Guid id)
    {
        var pair = AccessService.Pair(Me, id);
        await db.Friendships.Where(x => x.PairKey == pair).ExecuteDeleteAsync();
        return NoContent();
    }

    [HttpGet("admin/policy")]
    public async Task<ServerPolicy> Policy()
    {
        await access.Admin(Me);
        return await access.Policy();
    }

    [HttpPut("admin/policy")]
    public async Task<IActionResult> Policy(ServerPolicy p)
    {
        await access.Admin(Me);
        if (p.MaxUploadMb is < 1 or > 100)
            throw new ApiException(400, "Лимит файла: 1–100 МБ.");
        var row = await db.Settings.FindAsync("policy");
        if (row == null)
        {
            row = new() { Id = "policy" };
            db.Settings.Add(row);
        }
        row.Json = JsonSerializer.Serialize(p);
        access.Audit(Me, "policy.update", "server");
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("admin/deletions")]
    public async Task<object> Deletions()
    {
        await access.Admin(Me);
        return await db.Guilds.Where(x => x.DeletionRequested).ToArrayAsync();
    }

    [HttpPost("admin/deletions/{id:guid}/{approve:bool}")]
    public async Task<IActionResult> Approve(Guid id, bool approve)
    {
        await access.Admin(Me);
        var g =
            await db.Guilds.FindAsync(id)
            ?? throw new ApiException(404, "Пространство не найдено.");
        if (g.Managed || !g.DeletionRequested)
            throw new ApiException(400, "Нет заявки.");
        if (approve)
            db.Guilds.Remove(g);
        else
            g.DeletionRequested = false;
        access.Audit(Me, "guild.deletion-review", id);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("admin/audit")]
    public async Task<object> Audit()
    {
        await access.Admin(Me);
        return await db
            .AuditLog.AsNoTracking()
            .OrderByDescending(x => x.Id)
            .Take(200)
            .ToArrayAsync();
    }

    [HttpPost("admin/backup")]
    public async Task<IActionResult> Backup()
    {
        await access.Admin(Me);
        var path = Path.Combine(Path.GetTempPath(), $"qsock-{Guid.NewGuid():N}.db");
        try
        {
            await db.Database.OpenConnectionAsync();
            try
            {
                var source = (Microsoft.Data.Sqlite.SqliteConnection)db.Database.GetDbConnection();
                var settings = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Pooling = false,
                };
                await using var target = new Microsoft.Data.Sqlite.SqliteConnection(
                    settings.ToString()
                );
                await target.OpenAsync();
                source.BackupDatabase(target);
                await using var standalone = target.CreateCommand();
                standalone.CommandText = "PRAGMA journal_mode=DELETE";
                await standalone.ExecuteNonQueryAsync();
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
            var bytes = await System.IO.File.ReadAllBytesAsync(path);
            return File(
                bytes,
                "application/octet-stream",
                $"qsock-{DateTime.UtcNow:yyyyMMdd-HHmm}.db"
            );
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
}
