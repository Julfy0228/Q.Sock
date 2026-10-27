namespace Q.Sock.Server.Data;

public sealed class ChatListPreview
{
    public Guid ChatId { get; set; }
    public string? LastMessagePreview { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
}
