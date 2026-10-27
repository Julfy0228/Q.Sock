namespace Q.Sock.Server.Entities;

public sealed class GuildEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    public bool Managed { get; set; }
    public bool DeletionRequested { get; set; }
}

public sealed class GuildMemberEntity
{
    public Guid GuildId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "member";
    public string GroupsJson { get; set; } = "[]";
    public bool Active { get; set; } = true;
}

public sealed class ChatEntity
{
    public Guid Id { get; set; }
    public Guid? GuildId { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "group";
    public string? DirectKey { get; set; }
    public bool Managed { get; set; }
}

public sealed class ChatParticipantEntity
{
    public Guid ChatId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "member";
    public bool CanWrite { get; set; } = true;
    public bool CanSpeak { get; set; } = true;
    public bool CanConnect { get; set; } = true;
    public bool CanShare { get; set; } = true;
    public bool Active { get; set; } = true;
}

public sealed class AttachmentEntity
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }
    public Guid UploadedBy { get; set; }
    public Guid? MessageId { get; set; }
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public string StorageKey { get; set; } = "";
    public long Size { get; set; }
}

public sealed class MessageReceiptEntity
{
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
    public DateTime ReadAtUtc { get; set; }
}

public sealed class FriendshipEntity
{
    public Guid Id { get; set; }
    public Guid FromId { get; set; }
    public Guid ToId { get; set; }
    public string PairKey { get; set; } = "";
    public string Status { get; set; } = "pending";
}

public sealed class CallEntity
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }
    public Guid OrganizerId { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = "live";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartsAtUtc { get; set; }
    public int DurationMinutes { get; set; } = 90;
    public bool Reminder { get; set; }
    public bool ReminderSent { get; set; }
    public string? ContactEmail { get; set; }
    public string MaterialsJson { get; set; } = "[]";
    public bool CalendarPublished { get; set; }
}

public sealed class CallParticipantEntity
{
    public Guid CallId { get; set; }
    public Guid UserId { get; set; }
    public bool Removed { get; set; }
    public bool Muted { get; set; }
    public bool CanShare { get; set; } = true;
}

public sealed class IntegrationIdentityEntity
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = "";
    public string Subject { get; set; } = "";
    public Guid UserId { get; set; }
}

public sealed class LaunchNonceEntity
{
    public string Id { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public string Payload { get; set; } = "";
}

public sealed class AuditEntity
{
    public long Id { get; set; }
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SettingEntity
{
    public string Id { get; set; } = "";
    public string Json { get; set; } = "{}";
}
