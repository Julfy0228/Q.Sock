namespace Q.Sock.Core;

public interface IAccountPersistenceStatus
{
    bool SessionOnly { get; }
}

public sealed class AccountStorageException(string message, Exception innerException)
    : IOException(message, innerException);

public sealed class SessionFallbackAccountRepository(IAccountRepository storage)
    : IAccountRepository,
        IAccountPersistenceStatus
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private List<SavedAccount> current = [];
    public bool SessionOnly { get; private set; }

    public async Task<List<SavedAccount>> LoadAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (!SessionOnly)
                current = await storage.LoadAsync();
            return current.ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SessionOnly = true;
            return current.ToList();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(IEnumerable<SavedAccount> accounts)
    {
        var snapshot = accounts.ToList();
        await gate.WaitAsync();
        try
        {
            current = snapshot;
            if (!SessionOnly)
                await storage.SaveAsync(snapshot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SessionOnly = true;
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<string> GetOrCreateDeviceIdAsync() => storage.GetOrCreateDeviceIdAsync();
}
