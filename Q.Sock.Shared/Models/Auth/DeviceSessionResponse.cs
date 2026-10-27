using System;

namespace Q.Sock.Shared.Models.Auth;

public sealed class DeviceSessionResponse
{
    public Guid Id { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastActivityAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsCurrent { get; set; }
}
