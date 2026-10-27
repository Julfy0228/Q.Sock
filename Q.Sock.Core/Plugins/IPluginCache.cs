using System.Security.Cryptography;
using System.Text;

namespace Q.Sock.Core.Plugins;

public interface IPluginCache
{
    Task<byte[]?> Read(string server, string key);
    Task Write(string server, string key, byte[] bytes);
    Task Prune(string server, IReadOnlyCollection<string> keep);
}

public static class PluginCacheKeys
{
    public static string Server(Uri server) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(server.AbsoluteUri)));

    public static string Package(PluginPackage package)
    {
        package.ValidateMetadata();
        return $"{package.Id}-{package.Target}-{package.Sha256.ToLowerInvariant()}";
    }
}

public sealed class FilePluginCache(string directory) : IPluginCache
{
    private static void Validate(string server, string key)
    {
        if (
            !System.Text.RegularExpressions.Regex.IsMatch(server, "^[A-F0-9]{64}$")
            || !System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z0-9-]+$")
        )
            throw new ArgumentException("Некорректный ключ кэша.");
    }

    public async Task<byte[]?> Read(string server, string key)
    {
        Validate(server, key);
        var path = Path.Combine(directory, server, key + ".dll");
        return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
    }

    public async Task Write(string server, string key, byte[] bytes)
    {
        Validate(server, key);
        var folder = Path.Combine(directory, server);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, key + ".dll");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes);
        File.Move(temporary, path, overwrite: true);
    }

    public Task Prune(string server, IReadOnlyCollection<string> keep)
    {
        Validate(server, "module");
        var folder = Path.Combine(directory, server);
        if (Directory.Exists(folder))
            foreach (var path in Directory.EnumerateFiles(folder, "*.dll"))
                if (!keep.Contains(Path.GetFileNameWithoutExtension(path)))
                    File.Delete(path);
        return Task.CompletedTask;
    }
}
