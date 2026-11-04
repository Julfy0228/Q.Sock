using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Data;
using Q.Sock.Server.Entities;
using Q.Sock.Server.Modules;
using Q.Sock.Server.Services;
using Q.Sock.Shared.Models;
using Q.Sock.Shared.Models.Auth;

namespace Q.Sock.Server.Plugins.Moodle;

public sealed class MoodleModule : IMessengerModule
{
    private const string DescriptorPath = "/modules/moodle/ui.json";
    private static readonly byte[] Descriptor = JsonSerializer.SerializeToUtf8Bytes(
        new
        {
            id = "moodle",
            title = "Учебные курсы",
            version = "2",
        }
    );
    public string Id => "moodle";

    public bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue<bool?>("Moodle:Enabled") ?? true;

    public void ConfigureServices(IServiceCollection services, IConfiguration cfg)
    {
        services.AddScoped(provider => new MoodleDbContext(
            new DbContextOptionsBuilder<MoodleDbContext>()
                .UseSqlite(
                    provider.GetRequiredService<ChatDbContext>().Database.GetDbConnection(),
                    options => options.MigrationsHistoryTable("__QsockMoodleMigrations")
                )
                .Options
        ));
        services.AddScoped<MoodleService>();
        services.AddScoped<IExternalAuthenticationProvider>(provider =>
            provider.GetRequiredService<MoodleService>()
        );
        services.AddHostedService<LessonReminderService>();
    }

    public async Task Initialize(IServiceProvider services) =>
        await services.GetRequiredService<MoodleDbContext>().Database.MigrateAsync();

    public UiModule Describe(IWebHostEnvironment env)
    {
        return new()
        {
            Id = Id,
            Title = "Запланировать занятие",
            Version = "1",
            AssetUrl = DescriptorPath,
            Sha256 = Convert.ToHexString(SHA256.HashData(Descriptor)).ToLowerInvariant(),
            ActionUrl = "/api/modules/moodle/lessons",
            Fields =
            [
                new()
                {
                    Name = "title",
                    Label = "Название занятия",
                    Required = true,
                },
                new()
                {
                    Name = "chatId",
                    Label = "Учебная комната",
                    Kind = "course",
                    Required = true,
                },
                new()
                {
                    Name = "startsAtUtc",
                    Label = "Дата и время",
                    Kind = "datetime-local",
                    Required = true,
                },
                new()
                {
                    Name = "durationMinutes",
                    Label = "Продолжительность, минут",
                    Kind = "range",
                },
                new()
                {
                    Name = "contactEmail",
                    Label = "Почта для связи",
                    Kind = "email",
                },
                new()
                {
                    Name = "reminder",
                    Label = "Напомнить участникам",
                    Kind = "checkbox",
                },
                new()
                {
                    Name = "materials",
                    Label = "Материалы занятия: ссылки по одной на строку",
                    Kind = "carousel",
                },
            ],
        };
    }

    public void MapEndpoints(WebApplication app)
    {
        app.MapGet(DescriptorPath, () => Results.Bytes(Descriptor, "application/json"));
        app.MapGet(
            "/api/modules/moodle/ui.json",
            () => Results.Bytes(Descriptor, "application/json")
        );
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        app.MapPost(
            "/api/modules/moodle/sync",
            async (HttpRequest request, MoodleService moodle) =>
            {
                using var reader = new StreamReader(request.Body);
                var body = await reader.ReadToEndAsync();
                var c = await moodle.Verify(request, body);
                await moodle.Sync(
                    c,
                    JsonSerializer.Deserialize<MoodleSnapshot>(body, json)
                        ?? throw new ApiException(400, "Пустой снимок.")
                );
                return Results.Ok(new { synced = true });
            }
        );
        app.MapPost(
            "/api/modules/moodle/launch",
            async (
                HttpRequest request,
                MoodleService moodle,
                ChatDbContext db,
                MoodleDbContext integration,
                AccessService access
            ) =>
            {
                using var reader = new StreamReader(request.Body);
                var body = await reader.ReadToEndAsync();
                var c = await moodle.Verify(request, body);
                var data =
                    JsonSerializer.Deserialize<MoodleLaunch>(body, json)
                    ?? throw new ApiException(400, "Пустой запрос.");
                var identity =
                    await db.ExternalIdentities.SingleOrDefaultAsync(x =>
                        x.Provider == c.Provider && x.Subject == data.Subject
                    ) ?? throw new ApiException(403, "Учётная запись ещё не синхронизирована.");
                await access.User(identity.UserId);
                Guid? chatId = null;
                if (data.CourseId != 0)
                {
                    var course =
                        await integration.CourseLinks.SingleOrDefaultAsync(x =>
                            x.ConnectionId == c.Id && x.ExternalCourseId == data.CourseId
                        ) ?? throw new ApiException(404, "Курс не синхронизирован.");
                    chatId = course.RoomId;
                    await access.Chat(chatId.Value, identity.UserId);
                    if (
                        data.CallId != null
                        && !await db.Calls.AnyAsync(x => x.Id == data.CallId && x.ChatId == chatId)
                    )
                        throw new ApiException(403, "Занятие не относится к курсу.");
                }
                var code = await LaunchCodes.Create(db, new(identity.UserId, chatId, data.CallId));
                return Results.Ok(new { code, expiresIn = 60 });
            }
        );
        app.MapGet(
                "/api/modules/moodle/courses",
                async (HttpContext context, ChatDbContext db, MoodleDbContext integration) =>
                {
                    var user = context.User.UserId();
                    var guilds = await db
                        .Guilds.Where(guild =>
                            db.GuildMembers.Any(member =>
                                member.GuildId == guild.Id && member.UserId == user && member.Active
                            )
                        )
                        .ToDictionaryAsync(guild => guild.Id, guild => guild.Name);
                    var ids = guilds.Keys.ToArray();
                    var courses = await integration
                        .CourseLinks.Where(link => ids.Contains(link.GuildId))
                        .ToArrayAsync();
                    return courses
                        .Select(link => new
                        {
                            link.Id,
                            link.ConnectionId,
                            link.ExternalCourseId,
                            link.ChatId,
                            link.RoomId,
                            name = guilds[link.GuildId],
                        })
                        .ToArray();
                }
            )
            .RequireAuthorization();
        app.MapPost(
                "/api/modules/moodle/lessons",
                async (
                    LessonRequest r,
                    HttpContext context,
                    ChatDbContext db,
                    MoodleDbContext integration,
                    AccessService access,
                    MessengerService messenger
                ) =>
                {
                    var user = context.User.UserId();
                    await access.ManageChat(r.ChatId, user);
                    if (!await integration.CourseLinks.AnyAsync(x => x.RoomId == r.ChatId))
                        throw new ApiException(400, "Выберите учебную комнату.");
                    if (
                        r.Title.Trim().Length is < 1 or > 100
                        || r.DurationMinutes is < 15 or > 480
                        || r.StartsAtUtc.ToUniversalTime() < DateTime.UtcNow.AddMinutes(-1)
                        || r.Materials.Length > 20
                    )
                        throw new ApiException(
                            400,
                            "Проверьте название, время и продолжительность занятия."
                        );
                    if (
                        !string.IsNullOrWhiteSpace(r.ContactEmail)
                        && !MailAddress.TryCreate(r.ContactEmail, out _)
                    )
                        throw new ApiException(400, "Некорректная почта.");
                    if (
                        r.Materials.Any(x =>
                            !Uri.TryCreate(x, UriKind.Absolute, out var uri)
                            || uri.Scheme is not ("https" or "http")
                        )
                    )
                        throw new ApiException(400, "Материалы должны быть ссылками HTTP/HTTPS.");
                    var call = new CallEntity
                    {
                        Id = Guid.NewGuid(),
                        ChatId = r.ChatId,
                        OrganizerId = user,
                        Title = r.Title.Trim(),
                        Status = "planned",
                        CreatedAtUtc = DateTime.UtcNow,
                        StartsAtUtc = r.StartsAtUtc.ToUniversalTime(),
                        DurationMinutes = r.DurationMinutes,
                        Reminder = r.Reminder,
                        ContactEmail = r.ContactEmail,
                        MaterialsJson = JsonSerializer.Serialize(r.Materials),
                    };
                    db.Calls.Add(call);
                    access.Audit(user, "lesson.plan", call.Id);
                    await db.SaveChangesAsync();
                    await messenger.NotifyChat(r.ChatId, "lesson", CallService.Dto(call));
                    return Results.Ok(CallService.Dto(call));
                }
            )
            .RequireAuthorization();
    }
}
