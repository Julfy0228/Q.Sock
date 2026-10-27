using System;

namespace Q.Sock.Shared.Models;

public sealed class User
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
}
