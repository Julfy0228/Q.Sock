using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Q.Sock.Server.Data;
using Q.Sock.Server.Entities;
using Q.Sock.Server.Modules;
using Q.Sock.Server.Services;

namespace Q.Sock.Server.Plugins.Moodle;

public sealed class MoodleConnection
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = "university";
    public string BaseUrl { get; set; } = "";
    public string Secret { get; set; } = "";
}

public sealed class MoodleUser
{
    public string Subject { get; set; } = "";
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool Suspended { get; set; }
    public bool IsStudent { get; set; }
}

public sealed class MoodleMember
{
    public string Subject { get; set; } = "";
    public string Role { get; set; } = "student";
    public string[] Groups { get; set; } = [];
}

public sealed class MoodleCourse
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public MoodleMember[] Members { get; set; } = [];
}

public sealed class MoodleSnapshot
{
    public MoodleUser[] Users { get; set; } = [];
    public MoodleCourse[] Courses { get; set; } = [];
    public bool FullSnapshot { get; set; }
}

public sealed class MoodleLaunch
{
    public string ConnectionId { get; set; } = "";
    public string Subject { get; set; } = "";
    public long CourseId { get; set; }
    public Guid? CallId { get; set; }
}

public sealed class MoodleService(
    ChatDbContext db,
    MoodleDbContext integration,
    UserManager<UserEntity> users,
    IConfiguration config,
    IHttpClientFactory http,
    AccessService access,
    MessengerService messenger
) : IExternalAuthenticationProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
    public string Id => "moodle";
    public bool AllowsLocalRegistration => false;
    public bool Enabled => config.GetValue<bool?>("Moodle:Enabled") ?? true;
    public MoodleConnection[] Connections =>
        config.GetSection("Moodle:Connections").Get<MoodleConnection[]>() ?? [];

    public MoodleConnection Connection(string id) =>
        Connections.SingleOrDefault(x => x.Id == id)
        ?? throw new ApiException(403, "Подключение Moodle не разрешено.");

    public async Task<UserEntity?> Authenticate(string username, string password)
    {
        if (!Enabled || Connections.Length == 0)
            return null;
        var c =
            Connections.FirstOrDefault(connection =>
                connection.Id == config["Moodle:IdentityAuthority"]
            ) ?? Connections[0];
        var client = http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        using var response = await client.PostAsync(
            c.BaseUrl.TrimEnd('/') + "/login/token.php",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    { "username", username },
                    { "password", password },
                    { "service", "qsock" },
                }
            )
        );
        using var token = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!token.RootElement.TryGetProperty("token", out var value))
            return null;
        using var identity = await client.PostAsync(
            c.BaseUrl.TrimEnd('/') + "/webservice/rest/server.php",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    { "wstoken", value.GetString()! },
                    { "wsfunction", "local_qsock_identity" },
                    { "moodlewsrestformat", "json" },
                }
            )
        );
        var data = await identity.Content.ReadFromJsonAsync<MoodleUser>(Json);
        if (data == null || string.IsNullOrWhiteSpace(data.Subject) || data.Suspended)
            return null;
        return await UpsertUser(c, data);
    }

    public async Task<UserEntity> UpsertUser(MoodleConnection c, MoodleUser source)
    {
        if (source.Subject.Length is < 1 or > 100)
            throw new ApiException(400, "Некорректный идентификатор пользователя.");
        var link = await db.ExternalIdentities.SingleOrDefaultAsync(x =>
            x.Provider == c.Provider && x.Subject == source.Subject
        );
        UserEntity u;
        if (link == null)
        {
            var key = Convert
                .ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(c.Provider + ":" + source.Subject))
                )
                .ToLowerInvariant();
            u = new() { Id = Guid.NewGuid(), UserName = "m" + key[..24] };
            var result = await users.CreateAsync(u);
            if (!result.Succeeded)
                throw new ApiException(409, "Не удалось создать учебную учётную запись.");
            db.ExternalIdentities.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    Provider = c.Provider,
                    Subject = source.Subject,
                    UserId = u.Id,
                }
            );
        }
        else
            u =
                await db.Users.FindAsync(link.UserId)
                ?? throw new ApiException(409, "Повреждена привязка учётной записи.");
        var authority =
            Connections.FirstOrDefault(connection =>
                connection.Provider == c.Provider
                && connection.Id == config["Moodle:IdentityAuthority"]
            ) ?? Connections.First(connection => connection.Provider == c.Provider);
        if (c.Id == authority.Id || link is null)
        {
            u.DisplayName = source.DisplayName;
            u.UniversityLogin = source.UserName;
            u.IsAdmin = c.Id == authority.Id && source.IsAdmin;
            u.IsStudent = source.IsStudent;
            u.IsSuspended = source.Suspended;
        }
        if (u.IsSuspended)
            await db
                .DeviceSessions.Where(x => x.UserId == u.Id && x.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(x => x.RevokedAtUtc, (DateTime?)DateTime.UtcNow)
                );
        await db.SaveChangesAsync();
        return u;
    }

    public async Task Sync(MoodleConnection c, MoodleSnapshot snapshot)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await integration.Database.UseTransactionAsync(transaction.GetDbTransaction());
        var userMap = new Dictionary<string, UserEntity>();
        foreach (var source in snapshot.Users)
            userMap[source.Subject] = await UpsertUser(c, source);
        foreach (var course in snapshot.Courses)
        {
            var link = await integration.CourseLinks.SingleOrDefaultAsync(x =>
                x.ConnectionId == c.Id && x.ExternalCourseId == course.Id
            );
            var teacher = course.Members.FirstOrDefault(x =>
                x.Role == "teacher" && userMap.ContainsKey(x.Subject)
            );
            var owner =
                teacher != null
                    ? userMap[teacher.Subject].Id
                    : userMap.Values.FirstOrDefault()?.Id
                        ?? await db.Users.Select(x => x.Id).FirstOrDefaultAsync();
            if (owner == Guid.Empty)
                continue;
            if (link == null)
            {
                var guild = new GuildEntity
                {
                    Id = Guid.NewGuid(),
                    OwnerId = owner,
                    Name = course.Name,
                    Managed = true,
                };
                var chat = new ChatEntity
                {
                    Id = Guid.NewGuid(),
                    GuildId = guild.Id,
                    OwnerId = owner,
                    Name = course.Name,
                    Type = "channel",
                    Managed = true,
                };
                var room = new ChatEntity
                {
                    Id = Guid.NewGuid(),
                    GuildId = guild.Id,
                    OwnerId = owner,
                    Name = "Занятие · " + course.Name,
                    Type = "room",
                    Managed = true,
                };
                db.Guilds.Add(guild);
                db.Chats.AddRange(chat, room);
                link = new()
                {
                    Id = Guid.NewGuid(),
                    ConnectionId = c.Id,
                    ExternalCourseId = course.Id,
                    GuildId = guild.Id,
                    ChatId = chat.Id,
                    RoomId = room.Id,
                };
                integration.CourseLinks.Add(link);
                await db.SaveChangesAsync();
                await integration.SaveChangesAsync();
            }
            var g = await db.Guilds.FindAsync(link.GuildId);
            g!.Name = course.Name;
            g.OwnerId = owner;
            link.SyncedAtUtc = DateTime.UtcNow;
            var old = await db.GuildMembers.Where(x => x.GuildId == link.GuildId).ToArrayAsync();
            foreach (var m in old)
                m.Active = false;
            var chatIds = await db
                .Chats.Where(x => x.GuildId == link.GuildId)
                .Select(x => x.Id)
                .ToArrayAsync();
            var participants = await db
                .ChatParticipants.Where(x => chatIds.Contains(x.ChatId))
                .ToArrayAsync();
            foreach (var p in participants)
                p.Active = false;
            foreach (var member in course.Members)
            {
                if (!userMap.TryGetValue(member.Subject, out var u) || u.IsSuspended)
                    continue;
                var role = member.Role == "teacher" ? "teacher" : "student";
                var m = old.SingleOrDefault(x => x.UserId == u.Id);
                if (m == null)
                {
                    m = new() { GuildId = link.GuildId, UserId = u.Id };
                    db.GuildMembers.Add(m);
                }
                m.Active = true;
                m.Role = role;
                m.GroupsJson = JsonSerializer.Serialize(member.Groups);
                foreach (var cid in chatIds)
                {
                    var p = participants.SingleOrDefault(x => x.ChatId == cid && x.UserId == u.Id);
                    if (p == null)
                    {
                        p = new() { ChatId = cid, UserId = u.Id };
                        db.ChatParticipants.Add(p);
                    }
                    p.Active = true;
                    p.Role = role;
                }
            }
            await db.SaveChangesAsync();
        }
        if (snapshot.FullSnapshot)
        {
            var seen = snapshot.Courses.Select(x => x.Id).ToArray();
            var stale = await integration
                .CourseLinks.Where(x =>
                    x.ConnectionId == c.Id && !seen.Contains(x.ExternalCourseId)
                )
                .Select(x => x.GuildId)
                .ToArrayAsync();
            await db
                .GuildMembers.Where(x => stale.Contains(x.GuildId))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Active, false));
            var cs = await db
                .Chats.Where(x => x.GuildId != null && stale.Contains(x.GuildId.Value))
                .Select(x => x.Id)
                .ToArrayAsync();
            await db
                .ChatParticipants.Where(x => cs.Contains(x.ChatId))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Active, false));
        }
        access.Audit(null, "moodle.sync", c.Id);
        await db.SaveChangesAsync();
        await integration.SaveChangesAsync();
        await transaction.CommitAsync();
        foreach (
            var link in await integration
                .CourseLinks.Where(x => x.ConnectionId == c.Id)
                .ToArrayAsync()
        )
        {
            await messenger.NotifyChat(link.ChatId, "courses");
            await messenger.NotifyChat(link.RoomId, "courses");
        }
    }

    public async Task<MoodleConnection> Verify(HttpRequest request, string body)
    {
        if (!Enabled)
            throw new ApiException(404, "Модуль выключен.");
        var c = Connection(request.Headers["X-QSock-Connection"].ToString());
        if (
            c.Secret.Length < 32
            || !long.TryParse(request.Headers["X-QSock-Time"], out var ts)
            || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts) > 120
        )
            throw new ApiException(401, "Запрос просрочен.");
        var signature = request.Headers["X-QSock-Signature"].ToString();
        var expected = Convert
            .ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(c.Secret),
                    Encoding.UTF8.GetBytes(ts + "\n" + body)
                )
            )
            .ToLowerInvariant();
        if (
            signature.Length != expected.Length
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(signature),
                Encoding.ASCII.GetBytes(expected)
            )
        )
            throw new ApiException(401, "Некорректная подпись.");
        db.LaunchNonces.Add(
            new()
            {
                Id = "request:" + c.Id + ":" + signature,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(3),
            }
        );
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new ApiException(409, "Запрос уже обработан.");
        }
        return c;
    }
}
