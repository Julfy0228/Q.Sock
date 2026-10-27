using System;

namespace Q.Sock.Shared.Models.Auth;

public sealed class LaunchResponse
{
    public AuthResponse Auth { get; set; } = new();
    public Guid? ChatId { get; set; }
    public Guid? CallId { get; set; }
}
