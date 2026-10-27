using System.Text.Json.Serialization;

namespace Q.Sock.Core;

public interface IAccountRepository
{
    Task<List<SavedAccount>> LoadAsync();
    Task SaveAsync(IEnumerable<SavedAccount> accounts);
    Task<string> GetOrCreateDeviceIdAsync();
}

public sealed class SavedAccount
{
    public string ServerUrl { get; set; } = "http://localhost:5000/";
    public string UserName { get; set; } = "";
    public string CredentialId { get; set; } = "";
    public Guid SessionId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    [JsonIgnore]
    public string AccessToken { get; set; } = "";

    [JsonIgnore]
    public string RefreshToken { get; set; } = "";

    public override string ToString() => $"{UserName} · {new Uri(ServerUrl).Host}";
}
