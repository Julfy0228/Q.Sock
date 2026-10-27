using System.Security.Cryptography;
using System.Text.Json;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Q.Sock.Core;
using Q.Sock.Shared.Models.Auth;
using CipherMode = Javax.Crypto.CipherMode;

namespace Q.Sock.Android;

public sealed class AndroidAccountRepository(string directory) : IAccountRepository
{
    private const string KeyAlias = "Q.Sock.Accounts.v1";
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<List<SavedAccount>> LoadAsync()
    {
        var path = Path.Combine(directory, "accounts.secure");
        if (!File.Exists(path))
            return [];
        var encrypted = await File.ReadAllBytesAsync(path);
        if (encrypted.Length < 30 || encrypted[0] != 1)
            throw new InvalidDataException("Хранилище аккаунтов повреждено.");
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.DecryptMode, GetKey(), new GCMParameterSpec(128, encrypted[1..13]));
        var bytes = cipher.DoFinal(encrypted[13..])!;
        try
        {
            var records = JsonSerializer.Deserialize<AccountRecord[]>(bytes) ?? [];
            return records
                .Select(record => new SavedAccount
                {
                    ServerUrl = record.Server,
                    UserName = record.UserName,
                    AccessToken = record.Auth.AccessToken,
                    RefreshToken = record.Auth.RefreshToken,
                    SessionId = record.Auth.SessionId,
                    ExpiresAtUtc = record.Auth.ExpiresAtUtc,
                })
                .ToList();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public async Task SaveAsync(IEnumerable<SavedAccount> accounts)
    {
        await gate.WaitAsync();
        try
        {
            var records = accounts
                .Select(account => new AccountRecord
                {
                    Server = account.ServerUrl,
                    UserName = account.UserName,
                    Auth = new AuthResponse
                    {
                        AccessToken = account.AccessToken,
                        RefreshToken = account.RefreshToken,
                        SessionId = account.SessionId,
                        ExpiresAtUtc = account.ExpiresAtUtc,
                    },
                })
                .ToArray();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(records);
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.EncryptMode, GetKey());
            byte[] encrypted;
            try
            {
                encrypted = [1, .. cipher.GetIV()!, .. cipher.DoFinal(bytes)!];
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
            var path = Path.Combine(directory, "accounts.secure");
            await File.WriteAllBytesAsync(path + ".tmp", encrypted);
            File.Move(path + ".tmp", path, overwrite: true);
        }
        finally
        {
            gate.Release();
        }
    }

    private static IKey GetKey()
    {
        using var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        if (store.ContainsAlias(KeyAlias))
            return store.GetKey(KeyAlias, null)!;
        using var generator = KeyGenerator.GetInstance(
            KeyProperties.KeyAlgorithmAes!,
            "AndroidKeyStore"
        )!;
        using var parameters = new KeyGenParameterSpec.Builder(
            KeyAlias,
            KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt
        )
            .SetBlockModes(KeyProperties.BlockModeGcm!)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone!)
            .Build();
        generator.Init(parameters);
        return generator.GenerateKey()!;
    }

    public async Task<string> GetOrCreateDeviceIdAsync()
    {
        var path = Path.Combine(directory, "device.id");
        if (!File.Exists(path))
            await File.WriteAllTextAsync(path, Guid.NewGuid().ToString("N"));
        return await File.ReadAllTextAsync(path);
    }

    private sealed class AccountRecord
    {
        public string Server { get; set; } = "";
        public string UserName { get; set; } = "";
        public AuthResponse Auth { get; set; } = new();
    }
}
