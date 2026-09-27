using Microsoft.AspNetCore.Identity;

namespace Q.Sock.Server.Entities;

public sealed class UserEntity : IdentityUser<Guid>
{
    public new required Guid Id { get; init; }
    public new required string UserName { get; set; }
}
