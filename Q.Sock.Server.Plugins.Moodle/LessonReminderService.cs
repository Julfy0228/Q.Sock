using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Data;
using Q.Sock.Server.Modules;
using Q.Sock.Server.Services;
using Q.Sock.Shared.Models;

namespace Q.Sock.Server.Plugins.Moodle;

public sealed class LessonReminderService(
    IServiceScopeFactory scopes,
    ILogger<LessonReminderService> log
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        while (await timer.WaitForNextTickAsync(token))
        {
            try
            {
                await Tick(token);
            }
            catch (Exception e) when (!token.IsCancellationRequested)
            {
                log.LogWarning(e, "Не удалось обработать занятия; будет повтор.");
            }
        }
    }

    private async Task Tick(CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        var integration = scope.ServiceProvider.GetRequiredService<MoodleDbContext>();
        await db
            .LaunchNonces.Where(nonce => nonce.ExpiresAtUtc < DateTime.UtcNow)
            .ExecuteDeleteAsync(token);
        var moodle = scope.ServiceProvider.GetRequiredService<MoodleService>();
        if (!moodle.Enabled)
            return;
        var messenger = scope.ServiceProvider.GetRequiredService<MessengerService>();
        var http = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();
        http.Timeout = TimeSpan.FromSeconds(15);
        foreach (
            var call in await db
                .Calls.Where(x => x.StartsAtUtc != null && x.Status != "ended")
                .ToArrayAsync(token)
        )
        {
            var course = await integration.CourseLinks.SingleOrDefaultAsync(
                x => x.RoomId == call.ChatId,
                token
            );
            if (course == null)
                continue;
            if (!call.CalendarPublished)
            {
                try
                {
                    var c = moodle.Connection(course.ConnectionId);
                    var body = JsonSerializer.Serialize(
                        new
                        {
                            action = "calendar",
                            requestId = Guid.NewGuid(),
                            courseId = course.ExternalCourseId,
                            callId = call.Id,
                            title = call.Title,
                            startsAt = new DateTimeOffset(
                                DateTime.SpecifyKind(call.StartsAtUtc!.Value, DateTimeKind.Utc)
                            ).ToUnixTimeSeconds(),
                            duration = call.DurationMinutes * 60,
                        }
                    );
                    var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    using var request = new HttpRequestMessage(
                        HttpMethod.Post,
                        c.BaseUrl.TrimEnd('/') + "/local/qsock/api.php"
                    )
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    };
                    request.Headers.Add("X-QSock-Time", ts.ToString());
                    request.Headers.Add(
                        "X-QSock-Signature",
                        Convert
                            .ToHexString(
                                HMACSHA256.HashData(
                                    Encoding.UTF8.GetBytes(c.Secret),
                                    Encoding.UTF8.GetBytes(ts + "\n" + body)
                                )
                            )
                            .ToLowerInvariant()
                    );
                    using var response = await http.SendAsync(request, token);
                    response.EnsureSuccessStatusCode();
                    call.CalendarPublished = true;
                    await db.SaveChangesAsync(token);
                }
                catch (Exception exception) when (!token.IsCancellationRequested)
                {
                    log.LogWarning(
                        exception,
                        "Не удалось опубликовать занятие {CallId} в Moodle; будет повтор.",
                        call.Id
                    );
                }
            }
            if (
                call.Reminder
                && !call.ReminderSent
                && call.StartsAtUtc <= DateTime.UtcNow.AddMinutes(10)
                && call.StartsAtUtc >= DateTime.UtcNow.AddMinutes(-call.DurationMinutes)
            )
            {
                try
                {
                    var sender = await db
                        .ChatParticipants.Where(member =>
                            member.ChatId == course.ChatId
                            && member.Active
                            && member.CanWrite
                            && (member.Role == "teacher" || member.Role == "owner")
                            && db.Users.Any(user => user.Id == member.UserId && !user.IsSuspended)
                        )
                        .OrderByDescending(member => member.UserId == call.OrganizerId)
                        .Select(member => (Guid?)member.UserId)
                        .FirstOrDefaultAsync(token);
                    if (sender is null)
                        continue;
                    await messenger.Send(
                        course.ChatId,
                        sender.Value,
                        new SendMessageRequest
                        {
                            Text =
                                $"Занятие «{call.Title}» · {call.StartsAtUtc:dd.MM.yyyy HH:mm} UTC. Откройте раздел «Занятия» в учебном чате.",
                            ClientOperationId = call.Id,
                        }
                    );
                    call.ReminderSent = true;
                    await db.SaveChangesAsync(token);
                }
                catch (Exception exception) when (!token.IsCancellationRequested)
                {
                    log.LogWarning(
                        exception,
                        "Не удалось отправить напоминание о занятии {CallId}; будет повтор.",
                        call.Id
                    );
                }
            }
        }
    }
}
