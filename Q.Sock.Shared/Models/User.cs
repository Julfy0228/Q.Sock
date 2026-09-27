using System;

namespace Q.Sock.Shared.Models;

public sealed class User
{
	public Guid Id { get; set; }
	public string Name { get; set; } = string.Empty;
}
