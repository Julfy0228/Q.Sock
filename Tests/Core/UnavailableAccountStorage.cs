using Q.Sock.Core;

namespace Q.Sock.Tests.Core;

internal sealed class UnavailableAccountStorage : IAccountRepository
{
    public bool ReadFails { get; init; }
    public int Saves { get; private set; }

    public Task<List<SavedAccount>> LoadAsync()
    {
        if (ReadFails)
            throw new AccountStorageException(
                "Fixture read unavailable",
                new InvalidOperationException()
            );
        return Task.FromResult(new List<SavedAccount>());
    }

    public Task<string> GetOrCreateDeviceIdAsync() => Task.FromResult(Guid.NewGuid().ToString());

    public Task SaveAsync(IEnumerable<SavedAccount> accounts)
    {
        Saves++;
        throw new AccountStorageException(
            "Fixture storage unavailable",
            new InvalidOperationException()
        );
    }
}
