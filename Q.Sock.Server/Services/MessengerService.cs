using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Data;
using Q.Sock.Server.Entities;
using Q.Sock.Server.Hubs;
using Q.Sock.Shared.Models;

namespace Q.Sock.Server.Services;

public sealed class MessengerService(
    ChatDbContext db,
    AccessService access,
    IHubContext<ChatHub> hub
)
{
    public async Task NotifyChat(Guid chatId, string kind, object? value = null)
    {
        var users = await db
            .ChatParticipants.Where(x => x.ChatId == chatId && x.Active)
            .Select(x => x.UserId)
            .ToListAsync();
        await hub
            .Clients.Users(users.Select(user => user.ToString()))
            .SendAsync(
                "Changed",
                new
                {
                    kind,
                    chatId,
                    value,
                }
            );
    }

    public async Task<ChatMessage[]> History(
        Guid chatId,
        Guid userId,
        int offset = 0,
        string? search = null
    )
    {
        await access.Chat(chatId, userId);
        var skip = Math.Clamp(offset, 0, 100000);
        var filter = string.IsNullOrWhiteSpace(search) ? "" : search;
        var page = await db
            .Messages.FromSqlInterpolated(
                $"""
                SELECT * FROM "Messages"
                WHERE "ChatId" = {chatId} AND "DeletedAtUtc" IS NULL
                    AND ({filter} = '' OR instr("Text", {filter}) > 0)
                ORDER BY julianday("SentAt") DESC, "Id" DESC
                LIMIT 100 OFFSET {skip}
                """
            )
            .AsNoTracking()
            .Select(message => message.Id)
            .ToArrayAsync();
        var messages = await db
            .Messages.AsNoTracking()
            .Where(x => page.Contains(x.Id))
            .Join(
                db.Users,
                x => x.SenderId,
                u => u.Id,
                (x, u) =>
                    new ChatMessage
                    {
                        Id = x.Id,
                        ChatId = chatId,
                        SenderId = x.SenderId,
                        SenderName = u.DisplayName == "" ? u.UserName! : u.DisplayName,
                        Text = x.Text,
                        SentAtUtc = x.SentAt.UtcDateTime,
                        EditedAtUtc = x.EditedAtUtc,
                    }
            )
            .ToArrayAsync();
        var files = await db
            .Attachments.AsNoTracking()
            .Where(x => x.MessageId != null && page.Contains(x.MessageId.Value))
            .ToArrayAsync();
        var receipts = await db
            .MessageReceipts.Where(x => page.Contains(x.MessageId))
            .GroupBy(x => x.MessageId)
            .Select(x => new { Id = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count);
        foreach (var m in messages)
        {
            m.Attachments = files.Where(x => x.MessageId == m.Id).Select(FileDto).ToArray();
            m.ReadCount = receipts.GetValueOrDefault(m.Id);
        }
        return messages.OrderBy(x => x.SentAtUtc).ToArray();
    }

    public async Task<Guid> Send(Guid chatId, Guid userId, SendMessageRequest r)
    {
        await access.Chat(chatId, userId, true);
        var text = r.Text.Trim();
        if (
            text.Length > 2000
            || (text.Length == 0 && r.AttachmentIds.Length == 0)
            || r.AttachmentIds.Length > 10
            || r.ClientOperationId == Guid.Empty
        )
            throw new ApiException(
                400,
                "Сообщение: до 2000 символов, до 10 файлов, обязателен идентификатор операции."
            );
        var duplicate = await db.Messages.SingleOrDefaultAsync(x =>
            x.SenderId == userId && x.ClientOperationId == r.ClientOperationId
        );
        if (duplicate != null)
        {
            if (duplicate.ChatId != chatId)
                throw new ApiException(409, "Операция уже использована в другом чате.");
            return duplicate.Id;
        }
        var ids = r.AttachmentIds.Distinct().ToArray();
        var files = await db
            .Attachments.Where(x =>
                ids.Contains(x.Id)
                && x.ChatId == chatId
                && x.UploadedBy == userId
                && x.MessageId == null
            )
            .ToArrayAsync();
        if (files.Length != ids.Length)
            throw new ApiException(400, "Файл не найден или уже прикреплён.");
        var m = new MessageEntity
        {
            Id = Guid.NewGuid(),
            ChatId = chatId,
            SenderId = userId,
            Text = text,
            SentAt = DateTimeOffset.UtcNow,
            ClientOperationId = r.ClientOperationId,
        };
        db.Messages.Add(m);
        foreach (var file in files)
            file.MessageId = m.Id;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var existing = await db.Messages.SingleOrDefaultAsync(x =>
                x.SenderId == userId && x.ClientOperationId == r.ClientOperationId
            );
            if (existing == null || existing.ChatId != chatId)
                throw;
            return existing.Id;
        }
        await NotifyChat(chatId, "message", new { id = m.Id });
        return m.Id;
    }

    public static FileInfoDto FileDto(AttachmentEntity x) =>
        new()
        {
            Id = x.Id,
            Name = x.Name,
            ContentType = x.ContentType,
            Size = x.Size,
        };
}
