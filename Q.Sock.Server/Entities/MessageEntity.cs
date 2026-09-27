namespace Q.Sock.Server.Entities;

public sealed class MessageEntity
{
    public required Guid Id { get; init; }
    public required Guid SenderId { get; init; }
    public required string Text { get; set; }
    public DateTimeOffset SentAt { get; init; }
}
