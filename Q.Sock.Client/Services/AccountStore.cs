using System.Text.Json;
using System.Text.Json.Serialization;
using ktsu.CredentialCache;
using ktsu.CredentialCache.Storage;
using ktsu.Semantics.Strings;
using Q.Sock.Core;

namespace Q.Sock.Client.Services;

public sealed class AccountStore : IAccountRepository
{
    private const string StoreFileName = "accounts.json";
    private const string DeviceIdFileName = "device.id";
    private static readonly CredentialCache CredentialCache = new(CreateCredentialStore());
    private readonly string _storePath;
    private readonly string _deviceIdPath;
    private readonly SemaphoreSlim saveGate = new(1, 1);

    public AccountStore(string? profileDirectory = null)
    {
        var directory =
            profileDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Q.Sock"
            );
        Directory.CreateDirectory(directory);
        _storePath = Path.Combine(directory, StoreFileName);
        _deviceIdPath = Path.Combine(directory, DeviceIdFileName);
    }

    public async Task<List<SavedAccount>> LoadAsync()
    {
        if (!File.Exists(_storePath))
            return [];

        List<SavedAccount> accounts;
        try
        {
            var json = await File.ReadAllTextAsync(_storePath);
            accounts = JsonSerializer.Deserialize<List<SavedAccount>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }

        var loaded = new List<SavedAccount>();
        foreach (var account in accounts)
        {
            if (string.IsNullOrWhiteSpace(account.CredentialId))
                continue;

            var token = await ReadCredential(account.CredentialId);
            if (token is null)
                continue;

            try
            {
                var secrets = JsonSerializer.Deserialize<AccountSecrets>(token.Token.ToString());
                if (secrets is null)
                    continue;

                account.AccessToken = secrets.AccessToken;
                account.RefreshToken = secrets.RefreshToken;
                loaded.Add(account);
            }
            catch (JsonException)
            {
                await Task.Run(() => CredentialCache.Remove(CreatePersona(account.CredentialId)));
            }
        }

        return loaded;
    }

    public async Task SaveAsync(IEnumerable<SavedAccount> accounts)
    {
        var snapshot = accounts.ToArray();
        await saveGate.WaitAsync();
        try
        {
            await SaveCore(snapshot);
        }
        finally
        {
            saveGate.Release();
        }
    }

    private async Task SaveCore(IEnumerable<SavedAccount> accounts)
    {
        var accountList = accounts.ToList();
        var previousAccounts = await ReadMetadataAsync();
        foreach (var account in accountList)
        {
            if (string.IsNullOrWhiteSpace(account.CredentialId))
                account.CredentialId = CredentialCache.CreatePersonaGUID().ToString();

            var secrets = JsonSerializer.Serialize(
                new AccountSecrets
                {
                    AccessToken = account.AccessToken,
                    RefreshToken = account.RefreshToken,
                }
            );
            var credential = new CredentialWithToken
            {
                Token = SemanticString<CredentialToken>.Create(secrets),
            };
            try
            {
                await Task.Run(() =>
                    CredentialCache.AddOrReplace(CreatePersona(account.CredentialId), credential)
                );
            }
            catch (Exception exception)
            {
                throw new AccountStorageException(
                    "Защищённое хранилище аккаунтов недоступно.",
                    exception
                );
            }
        }

        var temporaryPath = _storePath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(accountList));
        File.Move(temporaryPath, _storePath, overwrite: true);

        var currentIds = accountList
            .Select(account => account.CredentialId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (
            var previous in previousAccounts.Where(account =>
                !string.IsNullOrWhiteSpace(account.CredentialId)
                && !currentIds.Contains(account.CredentialId)
            )
        )
            await Task.Run(() => CredentialCache.Remove(CreatePersona(previous.CredentialId)));
    }

    public async Task<string> GetOrCreateDeviceIdAsync()
    {
        if (File.Exists(_deviceIdPath))
        {
            var savedId = (await File.ReadAllTextAsync(_deviceIdPath)).Trim();
            if (Guid.TryParse(savedId, out _))
                return savedId;
        }

        var deviceId = Guid.NewGuid().ToString("D");
        await File.WriteAllTextAsync(_deviceIdPath, deviceId);
        return deviceId;
    }

    private async Task<List<SavedAccount>> ReadMetadataAsync()
    {
        if (!File.Exists(_storePath))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(_storePath);
            return JsonSerializer.Deserialize<List<SavedAccount>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static PersonaGUID CreatePersona(string credentialId) =>
        SemanticString<PersonaGUID>.Create(credentialId);

    private static async Task<CredentialWithToken?> ReadCredential(string credentialId)
    {
        try
        {
            return await Task.Run(() =>
                CredentialCache.TryGet(CreatePersona(credentialId), out var credential)
                    ? credential as CredentialWithToken
                    : null
            );
        }
        catch (Exception exception)
        {
            throw new AccountStorageException(
                "Защищённое хранилище аккаунтов недоступно.",
                exception
            );
        }
    }

    private static ICredentialStore CreateCredentialStore()
    {
        if (
            !OperatingSystem.IsWindows()
            && !OperatingSystem.IsMacOS()
            && !OperatingSystem.IsLinux()
        )
            throw new PlatformNotSupportedException(
                "A native credential-store provider is not configured for this platform."
            );

        return CredentialStoreFactory.CreateDefault("Q.Sock.Client");
    }

    private sealed class AccountSecrets
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
    }
}
