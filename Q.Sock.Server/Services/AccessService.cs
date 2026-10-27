using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Data;
using Q.Sock.Server.Entities;
using Q.Sock.Shared.Models;

namespace Q.Sock.Server.Services;

public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public static class PrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new ApiException(401, "Требуется вход.");
}

public sealed class AccessService(ChatDbContext db)
{
    public async Task<UserEntity> User(Guid id) =>
        await db.Users.FindAsync(id) is { IsSuspended: false } u
            ? u
            : throw new ApiException(403, "Аккаунт недоступен.");

    public async Task Admin(Guid id)
    {
        if (!(await User(id)).IsAdmin)
            throw new ApiException(403, "Только администратор сервера.");
    }

    public async Task<ChatParticipantEntity> Chat(Guid chatId, Guid userId, bool write = false)
    {
        await User(userId);
        var p = await db.ChatParticipants.FindAsync(chatId, userId);
        if (p is not { Active: true })
            throw new ApiException(403, "Нет доступа к чату.");
        if (write && !p.CanWrite)
            throw new ApiException(403, "Отправка сообщений ограничена модератором.");
        return p;
    }

    public async Task<ChatEntity> ManageChat(Guid chatId, Guid userId)
    {
        var p = await Chat(chatId, userId);
        var chat =
            await db.Chats.FindAsync(chatId) ?? throw new ApiException(404, "Чат не найден.");
        if (p.Role is not ("owner" or "moderator" or "teacher") && !(await User(userId)).IsAdmin)
            throw new ApiException(403, "Недостаточно прав.");
        return chat;
    }

    public async Task<GuildEntity> ManageGuild(Guid guildId, Guid userId)
    {
        var guild =
            await db.Guilds.FindAsync(guildId)
            ?? throw new ApiException(404, "Пространство не найдено.");
        var member = await db.GuildMembers.FindAsync(guildId, userId);
        if (
            member is not { Active: true }
            || (
                member.Role is not ("owner" or "moderator" or "teacher")
                && !(await User(userId)).IsAdmin
            )
        )
            throw new ApiException(403, "Недостаточно прав.");
        return guild;
    }

    public async Task<ServerPolicy> Policy() =>
        await db.Settings.FindAsync("policy") is { } row
            ? System.Text.Json.JsonSerializer.Deserialize<ServerPolicy>(row.Json) ?? new()
            : new();

    public void Audit(Guid? actor, string action, object target) =>
        db.AuditLog.Add(
            new()
            {
                ActorId = actor,
                Action = action,
                Target = target.ToString() ?? "",
            }
        );

    public static string Pair(Guid a, Guid b) =>
        string.Join(":", new[] { a.ToString("N"), b.ToString("N") }.Order(StringComparer.Ordinal));
}
