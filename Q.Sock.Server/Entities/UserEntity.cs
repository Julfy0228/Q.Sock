using Microsoft.AspNetCore.Identity;

namespace Q.Sock.Server.Entities;

public sealed class UserEntity : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool IsStudent { get; set; }
    public bool IsSuspended { get; set; }
    public string UniversityLogin { get; set; } = "";
}
