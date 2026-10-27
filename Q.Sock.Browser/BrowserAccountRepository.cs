using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Q.Sock.Core;

namespace Q.Sock.Browser;

internal static partial class BrowserStorage
{
    [JSImport("origin", "qsock")]
    public static partial string Origin();

    [JSImport("readAccounts", "qsock")]
    public static partial string ReadAccounts();

    [JSImport("writeAccounts", "qsock")]
    public static partial void WriteAccounts(string value);

    [JSImport("deviceId", "qsock")]
    public static partial string DeviceId();

    [JSImport("consumeLaunchCode", "qsock")]
    public static partial string ConsumeLaunchCode();
}

public sealed class BrowserAccountRepository : IAccountRepository
{
    public Task<List<SavedAccount>> LoadAsync()
    {
        var records =
            JsonSerializer.Deserialize(
                BrowserStorage.ReadAccounts(),
                BrowserJsonContext.Default.ListBrowserAccount
            ) ?? [];
        var accounts = records
            .Where(record => record.ServerUrl == BrowserStorage.Origin())
            .Select(record => new SavedAccount
            {
                ServerUrl = record.ServerUrl,
                UserName = record.UserName,
                SessionId = record.SessionId,
                ExpiresAtUtc = record.ExpiresAtUtc,
                AccessToken = record.AccessToken,
                RefreshToken = record.RefreshToken,
            })
            .ToList();
        return Task.FromResult(accounts);
    }

    public Task SaveAsync(IEnumerable<SavedAccount> accounts)
    {
        var records = accounts
            .Where(account => account.ServerUrl == BrowserStorage.Origin())
            .Select(account => new BrowserAccount
            {
                ServerUrl = account.ServerUrl,
                UserName = account.UserName,
                SessionId = account.SessionId,
                ExpiresAtUtc = account.ExpiresAtUtc,
                AccessToken = account.AccessToken,
                RefreshToken = account.RefreshToken,
            })
            .ToList();
        BrowserStorage.WriteAccounts(
            JsonSerializer.Serialize(records, BrowserJsonContext.Default.ListBrowserAccount)
        );
        return Task.CompletedTask;
    }

    public Task<string> GetOrCreateDeviceIdAsync() => Task.FromResult(BrowserStorage.DeviceId());
}

internal sealed class BrowserAccount
{
    public string ServerUrl { get; set; } = "";
    public string UserName { get; set; } = "";
    public Guid SessionId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
}
