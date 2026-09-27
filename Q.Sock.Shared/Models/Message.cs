using System;

namespace Q.Sock.Shared.Models;

public sealed class Message
{
    public Guid Id { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }
    public string DisplayText => $"[{SentAt:HH:mm:ss}] {SenderName}: {Text}";
}