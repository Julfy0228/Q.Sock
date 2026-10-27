namespace Q.Sock.Core;

public interface IClientPreferences
{
    Task<string?> Read(string key);
    Task Write(string key, string value);
}

public sealed class FileClientPreferences(string directory) : IClientPreferences
{
    private readonly SemaphoreSlim writes = new(1, 1);

    private string FileName(string key)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z0-9-]+$"))
            throw new ArgumentException("Некорректное имя настройки.");
        return Path.Combine(directory, key + ".json");
    }

    public async Task<string?> Read(string key)
    {
        var path = FileName(key);
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
    }

    public async Task Write(string key, string value)
    {
        await writes.WaitAsync();
        try
        {
            var path = FileName(key);
            Directory.CreateDirectory(directory);
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, value);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            writes.Release();
        }
    }
}
