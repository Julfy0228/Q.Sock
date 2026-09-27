using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Q.Sock.Client.Services;

public sealed class AccountStore
{
    private const string StoreFileName = "accounts.dat";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Q.Sock.Client.AccountStore.v1");
    private readonly string _storePath;

    public AccountStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Q.Sock");
        Directory.CreateDirectory(directory);
        _storePath = Path.Combine(directory, StoreFileName);
    }

    public async Task<List<SavedAccount>> LoadAsync()
    {
        if (!OperatingSystem.IsWindows())
            return [];

        if (!File.Exists(_storePath))
            return [];

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(_storePath);
            var json = Encoding.UTF8.GetString(Unprotect(protectedBytes));
            return JsonSerializer.Deserialize<List<SavedAccount>>(json) ?? [];
        }
        catch (CryptographicException)
        {
            File.Delete(_storePath);
            return [];
        }
        catch (JsonException)
        {
            File.Delete(_storePath);
            return [];
        }
    }

    public async Task SaveAsync(IEnumerable<SavedAccount> accounts)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var json = JsonSerializer.Serialize(accounts);
        var protectedBytes = Protect(Encoding.UTF8.GetBytes(json));
        await File.WriteAllBytesAsync(_storePath, protectedBytes);
    }

    private static byte[] Protect(byte[] bytes)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);

        throw new PlatformNotSupportedException();
    }

    private static byte[] Unprotect(byte[] bytes)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);

        throw new PlatformNotSupportedException();
    }
}

public sealed class SavedAccount
{
    public string UserName { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
