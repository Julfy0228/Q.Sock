using System;

namespace Q.Sock.Shared.Models.Auth;

public sealed class RefreshRequest
{
    public Guid SessionId { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
}
