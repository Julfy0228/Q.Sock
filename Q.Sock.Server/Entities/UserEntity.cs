namespace Q.Sock.Server.Entities;

public sealed class UserEntity
{
    public required Guid Id { get; init; }
    public required string Name { get; set; }
}
