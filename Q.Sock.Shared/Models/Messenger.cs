#nullable enable
using System;
using System.Collections.Generic;

namespace Q.Sock.Shared.Models;

public sealed class Profile
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool IsStudent { get; set; }
}

public sealed class ChatInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "group";
    public Guid? GuildId { get; set; }
    public bool Managed { get; set; }
    public bool CanManage { get; set; }
    public string LastMessagePreview { get; set; } = "";
    public DateTimeOffset? LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
}

public sealed class GuildInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool Managed { get; set; }
    public bool CanManage { get; set; }
}

public sealed class MemberInfo
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "member";
    public string[] Groups { get; set; } = [];
    public bool CanWrite { get; set; }
    public bool CanSpeak { get; set; }
    public bool CanConnect { get; set; }
    public bool CanShare { get; set; }
}

public sealed class FileInfoDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
}

public sealed class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }
    public Guid SenderId { get; set; }
    public string SenderName { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime SentAtUtc { get; set; }
    public DateTime? EditedAtUtc { get; set; }
    public FileInfoDto[] Attachments { get; set; } = [];
    public int ReadCount { get; set; }
}

public sealed class CreateChat
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "group";
    public Guid? GuildId { get; set; }
    public Guid[] UserIds { get; set; } = [];
}

public sealed class NamedRequest
{
    public string Name { get; set; } = "";
}

public sealed class SendMessageRequest
{
    public string Text { get; set; } = "";
    public Guid ClientOperationId { get; set; }
    public Guid[] AttachmentIds { get; set; } = [];
}

public sealed class MemberUpdate
{
    public string Role { get; set; } = "member";
    public bool CanWrite { get; set; } = true;
    public bool CanSpeak { get; set; } = true;
    public bool CanConnect { get; set; } = true;
    public bool CanShare { get; set; } = true;
}

public sealed class CallRequest
{
    public Guid ChatId { get; set; }
    public Guid[] RingUserIds { get; set; } = [];
    public Guid[] ExcludedUserIds { get; set; } = [];
    public bool RingAll { get; set; } = true;
}

public sealed class CallInfo
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }
    public string Title { get; set; } = "";
    public Guid OrganizerId { get; set; }
    public string Status { get; set; } = "live";
    public DateTime? StartsAtUtc { get; set; }
    public int DurationMinutes { get; set; }
    public bool Reminder { get; set; }
    public string? ContactEmail { get; set; }
    public string[] Materials { get; set; } = [];
}

public sealed class LessonRequest
{
    public Guid ChatId { get; set; }
    public string Title { get; set; } = "";
    public DateTime StartsAtUtc { get; set; }
    public int DurationMinutes { get; set; } = 90;
    public bool Reminder { get; set; } = true;
    public string? ContactEmail { get; set; }
    public string[] Materials { get; set; } = [];
}

public sealed class FriendInfo
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Status { get; set; } = "pending";
    public bool Incoming { get; set; }
}

public sealed class ServerPolicy
{
    public bool StudentsCanCreateGuilds { get; set; } = true;
    public bool ConfirmStudentGuildDeletion { get; set; } = true;
    public bool AllowRegistration { get; set; } = true;
    public int MaxUploadMb { get; set; } = 25;
}

public sealed class ServerInfo
{
    public string Name { get; set; } = "Q.Sock";
    public string LoginHint { get; set; } = "Логин и пароль этого сервера";
    public string UiVersion { get; set; } = "1";
    public ServerPolicy Policy { get; set; } = new();
}

public sealed class UiManifest
{
    public string Version { get; set; } = "1";
    public List<UiModule> Modules { get; set; } = [];
}

public sealed class UiModule
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Version { get; set; } = "1";
    public string AssetUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public List<UiField> Fields { get; set; } = [];
    public string ActionUrl { get; set; } = "";
}

public sealed class UiField
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Kind { get; set; } = "text";
    public bool Required { get; set; }
}
