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
public sealed class MessengerController(
    ChatDbContext db,
    AccessService access,
    MessengerService messenger,
    IWebHostEnvironment env,
    IConfiguration configuration
) : ControllerBase
{
    private Guid Me => User.UserId();

    [HttpGet("me")]
    public async Task<Profile> MeInfo()
    {
        var u = await access.User(Me);
        return new()
        {
            Id = u.Id,
            UserName = u.UserName!,
            DisplayName = u.DisplayName,
            IsAdmin = u.IsAdmin,
            IsStudent = u.IsStudent,
        };
    }

    [HttpGet("users")]
    public async Task<object> Users([FromQuery] string search = "")
    {
        await access.User(Me);
        return await db
            .Users.AsNoTracking()
            .Where(x =>
                !x.IsSuspended
                && (
                    x.UserName!.Contains(search)
                    || x.DisplayName.Contains(search)
                    || x.UniversityLogin.Contains(search)
                )
            )
            .OrderBy(x => x.UserName)
            .Take(50)
            .Select(x => new
            {
                x.Id,
                x.UserName,
                x.DisplayName,
            })
            .ToArrayAsync();
    }

    [HttpGet("chats")]
    public async Task<object> Chats()
    {
        var result = await db
            .Chats.AsNoTracking()
            .Join(
                db.ChatParticipants.Where(x => x.UserId == Me && x.Active),
                c => c.Id,
                p => p.ChatId,
                (c, p) =>
                    new ChatInfo
                    {
                        Id = c.Id,
                        Name =
                            c.Type == "direct"
                                ? db.ChatParticipants.Where(other =>
                                        other.ChatId == c.Id && other.UserId != Me && other.Active
                                    )
                                    .Join(
                                        db.Users,
                                        other => other.UserId,
                                        user => user.Id,
                                        (other, user) =>
                                            user.DisplayName == ""
                                                ? user.UserName!
                                                : user.DisplayName
                                    )
                                    .FirstOrDefault()
                                    ?? c.Name
                                : c.Name,
                        Type = c.Type,
                        GuildId = c.GuildId,
                        Managed = c.Managed,
                        CanManage =
                            p.Role == "owner" || p.Role == "moderator" || p.Role == "teacher",
                    }
            )
            .ToArrayAsync();
        var previews = await db
            .Database.SqlQuery<ChatListPreview>(
                $"""
                SELECT c."Id" AS "ChatId",
                    (SELECT CASE WHEN m."Text" = '' THEN 'Вложение' ELSE substr(m."Text", 1, 100) END
                        FROM "Messages" m WHERE m."ChatId" = c."Id" AND m."DeletedAtUtc" IS NULL
                        ORDER BY m."SentAt" DESC, m.rowid DESC LIMIT 1) AS "LastMessagePreview",
                    (SELECT m."SentAt" FROM "Messages" m
                        WHERE m."ChatId" = c."Id" AND m."DeletedAtUtc" IS NULL
                        ORDER BY m."SentAt" DESC, m.rowid DESC LIMIT 1) AS "LastMessageAt",
                    (SELECT COUNT(*) FROM "Messages" m
                        WHERE m."ChatId" = c."Id" AND m."DeletedAtUtc" IS NULL AND m."SenderId" <> {Me}
                        AND NOT EXISTS(SELECT 1 FROM "MessageReceipts" r WHERE r."MessageId" = m."Id" AND r."UserId" = {Me})) AS "UnreadCount"
                FROM "Chats" c JOIN "ChatParticipants" p ON p."ChatId" = c."Id"
                WHERE p."UserId" = {Me} AND p."Active" = 1
                """
            )
            .ToDictionaryAsync(preview => preview.ChatId);
        foreach (var chat in result)
        {
            if (!previews.TryGetValue(chat.Id, out var preview))
                continue;
            chat.LastMessagePreview = preview.LastMessagePreview ?? "";
            chat.LastMessageAt = preview.LastMessageAt;
            chat.UnreadCount = preview.UnreadCount;
        }
        return result.OrderByDescending(chat => chat.LastMessageAt).ThenBy(chat => chat.Name);
    }

    [HttpPost("chats")]
    public async Task<object> CreateChat(CreateChat r)
    {
        if (r.Type is not ("direct" or "group" or "channel" or "room"))
            throw new ApiException(400, "Неизвестный вид чата.");
        if (r.Type is "channel" or "room" && r.GuildId == null)
            throw new ApiException(400, "Канал и комната должны принадлежать пространству.");
        if (r.Type is "direct" or "group" && r.GuildId != null)
            throw new ApiException(
                400,
                "Личные и групповые чаты существуют отдельно от пространств."
            );
        var ids = r.UserIds.Append(Me).Distinct().ToArray();
        if (
            ids.Length > 200
            || await db.Users.CountAsync(x => ids.Contains(x.Id) && !x.IsSuspended) != ids.Length
        )
            throw new ApiException(400, "Проверьте участников.");
        string? pair = null;
        if (r.Type == "direct")
        {
            if (ids.Length != 2)
                throw new ApiException(400, "В личном чате два участника.");
            pair = AccessService.Pair(ids[0], ids[1]);
            var found = await db.Chats.SingleOrDefaultAsync(x => x.DirectKey == pair);
            if (found != null)
                return new { id = found.Id };
        }
        var guild = r.GuildId == null ? null : await access.ManageGuild(r.GuildId.Value, Me);
        if (guild?.Managed == true)
            throw new ApiException(403, "Учебные каналы создаёт модуль Moodle.");
        var name = r.Name.Trim();
        if (name.Length > 100 || (name.Length == 0 && r.Type != "direct"))
            throw new ApiException(400, "Название: 1–100 символов.");
        if (r.Type == "direct")
            name = string.Join(
                " ↔ ",
                await db.Users.Where(x => ids.Contains(x.Id)).Select(x => x.UserName).ToArrayAsync()
            );
        var chat = new ChatEntity
        {
            Id = Guid.NewGuid(),
            OwnerId = Me,
            Name = name,
            Type = r.Type,
            GuildId = r.GuildId,
            DirectKey = pair,
        };
        db.Chats.Add(chat);
        if (guild != null)
            ids = await db
                .GuildMembers.Where(x => x.GuildId == guild.Id && x.Active)
                .Select(x => x.UserId)
                .ToArrayAsync();
        foreach (var id in ids)
            db.ChatParticipants.Add(
                new()
                {
                    ChatId = chat.Id,
                    UserId = id,
                    Role = id == Me ? "owner" : "member",
                }
            );
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (pair != null)
        {
            db.ChangeTracker.Clear();
            return new { id = (await db.Chats.SingleAsync(x => x.DirectKey == pair)).Id };
        }
        await messenger.NotifyChat(chat.Id, "chats");
        return new { id = chat.Id };
    }

    [HttpPatch("chats/{id:guid}")]
    public async Task<object> Rename(Guid id, NamedRequest r)
    {
        var c = await access.ManageChat(id, Me);
        if (c.Managed || c.Type == "direct")
            throw new ApiException(403, "Название этого чата управляется автоматически.");
        c.Name = ValidName(r.Name);
        await db.SaveChangesAsync();
        await messenger.NotifyChat(id, "chats");
        return c;
    }

    [HttpDelete("chats/{id:guid}")]
    public async Task<IActionResult> DeleteChat(Guid id)
    {
        var c = await access.ManageChat(id, Me);
        if (c.Managed)
            throw new ApiException(403, "Учебный канал нельзя удалить.");
        await messenger.NotifyChat(id, "chat-deleted");
        db.Chats.Remove(c);
        access.Audit(Me, "chat.delete", id);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("chats/{id:guid}/members")]
    public async Task<object> Members(Guid id)
    {
        await access.Chat(id, Me);
        var c = await db.Chats.FindAsync(id);
        var rows = await db
            .ChatParticipants.Where(x => x.ChatId == id && x.Active)
            .Join(
                db.Users,
                p => p.UserId,
                u => u.Id,
                (p, u) =>
                    new MemberInfo
                    {
                        UserId = u.Id,
                        UserName = u.UserName!,
                        DisplayName = u.DisplayName,
                        Role = p.Role,
                        CanWrite = p.CanWrite,
                        CanSpeak = p.CanSpeak,
                        CanConnect = p.CanConnect,
                        CanShare = p.CanShare,
                    }
            )
            .ToArrayAsync();
        if (c?.GuildId is Guid gid)
        {
            var groups = await db
                .GuildMembers.Where(x => x.GuildId == gid)
                .ToDictionaryAsync(x => x.UserId, x => x.GroupsJson);
            foreach (var m in rows)
                m.Groups =
                    JsonSerializer.Deserialize<string[]>(groups.GetValueOrDefault(m.UserId, "[]"))
                    ?? [];
        }
        return rows;
    }

    [HttpPut("chats/{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> Member(Guid id, Guid userId, MemberUpdate r)
    {
        var c = await access.ManageChat(id, Me);
        if (c.Type == "direct")
            throw new ApiException(403, "Состав личного чата неизменен.");
        if (!c.Managed && r.Role is not ("member" or "moderator"))
            throw new ApiException(400, "Назначать можно участника или модератора.");
        var p = await db.ChatParticipants.FindAsync(id, userId);
        if (c.Managed && (p == null || !p.Active))
            throw new ApiException(403, "Участников курса синхронизирует Moodle.");
        if (p?.Role == "owner" || userId == Me)
            throw new ApiException(403, "Нельзя менять владельца или собственные права.");
        if (
            !c.Managed
            && r.Role == "moderator"
            && p?.Role != "moderator"
            && (await access.Chat(id, Me)).Role != "owner"
            && !(await access.User(Me)).IsAdmin
        )
            throw new ApiException(403, "Модератора назначает владелец.");
        if (
            p?.Role == "moderator"
            && (await access.Chat(id, Me)).Role != "owner"
            && !(await access.User(Me)).IsAdmin
        )
            throw new ApiException(403, "Модератора меняет владелец.");
        await access.User(userId);
        if (
            c.GuildId is Guid gid
            && !await db.GuildMembers.AnyAsync(x =>
                x.GuildId == gid && x.UserId == userId && x.Active
            )
        )
            throw new ApiException(403, "Сначала добавьте участника в пространство.");
        if (p == null)
        {
            p = new() { ChatId = id, UserId = userId };
            db.ChatParticipants.Add(p);
        }
        if (!c.Managed)
            p.Role = r.Role;
        p.Active = true;
        p.CanWrite = r.CanWrite;
        p.CanSpeak = r.CanSpeak;
        p.CanConnect = r.CanConnect;
        p.CanShare = r.CanShare;
        access.Audit(Me, "member.permissions", $"{id}/{userId}");
        await db.SaveChangesAsync();
        await messenger.NotifyChat(id, "permissions", new { userId });
        return NoContent();
    }

    [HttpDelete("chats/{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> Remove(Guid id, Guid userId)
    {
        var c = await access.ManageChat(id, Me);
        if (c.Managed || c.Type == "direct")
            throw new ApiException(403, "Состав этого чата управляется автоматически.");
        var p =
            await db.ChatParticipants.FindAsync(id, userId)
            ?? throw new ApiException(404, "Участник не найден.");
        if (
            p.Role == "owner"
            || p.Role == "moderator" && (await access.Chat(id, Me)).Role != "owner"
        )
            throw new ApiException(403, "Недостаточно прав.");
        p.Active = false;
        await db.SaveChangesAsync();
        await messenger.NotifyChat(id, "members");
        return NoContent();
    }

    [HttpGet("chats/{id:guid}/messages")]
    public Task<ChatMessage[]> History(
        Guid id,
        [FromQuery] int offset = 0,
        [FromQuery] string? search = null
    ) => messenger.History(id, Me, offset, search);

    [HttpPost("chats/{id:guid}/messages")]
    public async Task<object> Send(Guid id, SendMessageRequest r) =>
        new { id = await messenger.Send(id, Me, r) };

    [HttpPatch("messages/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, SendMessageRequest r)
    {
        var m =
            await db.Messages.FindAsync(id) ?? throw new ApiException(404, "Сообщение не найдено.");
        await access.Chat(m.ChatId!.Value, Me, true);
        if (m.SenderId != Me)
            throw new ApiException(403, "Можно редактировать свои сообщения.");
        if (r.Text.Trim().Length is < 1 or > 2000)
            throw new ApiException(400, "Текст: 1–2000 символов.");
        m.Text = r.Text.Trim();
        m.EditedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await messenger.NotifyChat(m.ChatId.Value, "message");
        return NoContent();
    }

    [HttpDelete("messages/{id:guid}")]
    public async Task<IActionResult> DeleteMessage(Guid id)
    {
        var m =
            await db.Messages.FindAsync(id) ?? throw new ApiException(404, "Сообщение не найдено.");
        await access.Chat(m.ChatId!.Value, Me);
        if (m.SenderId != Me)
            await access.ManageChat(m.ChatId.Value, Me);
        m.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await messenger.NotifyChat(m.ChatId.Value, "message");
        return NoContent();
    }

    [HttpPost("chats/{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id)
    {
        await access.Chat(id, Me);
        var mids = await db
            .Messages.Where(x =>
                x.ChatId == id
                && x.SenderId != Me
                && !db.MessageReceipts.Any(r => r.MessageId == x.Id && r.UserId == Me)
            )
            .Select(x => x.Id)
            .ToArrayAsync();
        foreach (var mid in mids)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT OR IGNORE INTO MessageReceipts (MessageId,UserId,ReadAtUtc) VALUES ({mid},{Me},{DateTime.UtcNow})"
            );
        if (mids.Length > 0)
            await messenger.NotifyChat(id, "read");
        return NoContent();
    }

    [HttpPost("chats/{id:guid}/files"), RequestSizeLimit(104857600)]
    public async Task<FileInfoDto> Upload(Guid id, IFormFile file)
    {
        await access.Chat(id, Me, true);
        var policy = await access.Policy();
        if (file.Length == 0 || file.Length > policy.MaxUploadMb * 1024L * 1024)
            throw new ApiException(400, $"Размер файла: до {policy.MaxUploadMb} МБ.");
        var dir = configuration["Storage:Directory"] ?? Path.Combine(env.ContentRootPath, "storage");
        Directory.CreateDirectory(dir);
        var key = Guid.NewGuid().ToString("N");
        await using (var output = System.IO.File.Create(Path.Combine(dir, key)))
        {
            await file.CopyToAsync(output);
        }
        var entity = new AttachmentEntity
        {
            Id = Guid.NewGuid(),
            ChatId = id,
            UploadedBy = Me,
            Name = Path.GetFileName(file.FileName),
            ContentType = file.ContentType,
            StorageKey = key,
            Size = file.Length,
        };
        db.Attachments.Add(entity);
        await db.SaveChangesAsync();
        return MessengerService.FileDto(entity);
    }

    [HttpGet("files/{id:guid}")]
    public async Task<IActionResult> Download(Guid id)
    {
        var f =
            await db.Attachments.FindAsync(id) ?? throw new ApiException(404, "Файл не найден.");
        await access.Chat(f.ChatId, Me);
        if (f.MessageId == null && f.UploadedBy != Me)
            throw new ApiException(403, "Файл ещё не отправлен.");
        return PhysicalFile(
            Path.Combine(configuration["Storage:Directory"] ?? Path.Combine(env.ContentRootPath, "storage"), f.StorageKey),
            "application/octet-stream",
            f.Name
        );
    }

    internal static string ValidName(string name) =>
        name.Trim().Length is > 0 and <= 100
            ? name.Trim()
            : throw new ApiException(400, "Название: 1–100 символов.");
}
