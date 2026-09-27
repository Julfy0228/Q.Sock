namespace Q.Sock.Shared.Models.Auth;

public sealed class RegisterRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
