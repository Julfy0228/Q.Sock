using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Q.Sock.Core.Plugins;

namespace Q.Sock.Browser;

public sealed partial class BrowserPluginCache : IPluginCache
{
    [JSImport("pluginCache.read", "qsock")]
    private static partial Task<string> ReadEntry(string server, string key);

    [JSImport("pluginCache.write", "qsock")]
    private static partial Task WriteEntry(string server, string key, string bytes);

    [JSImport("pluginCache.prune", "qsock")]
    private static partial Task PruneEntries(string server, string keep);

    public async Task<byte[]?> Read(string server, string key)
    {
        var value = await ReadEntry(server, key);
        return value.Length == 0 ? null : Convert.FromBase64String(value);
    }

    public Task Write(string server, string key, byte[] bytes) =>
        WriteEntry(server, key, Convert.ToBase64String(bytes));

    public Task Prune(string server, IReadOnlyCollection<string> keep) =>
        PruneEntries(
            server,
            JsonSerializer.Serialize(keep.ToArray(), BrowserJsonContext.Default.StringArray)
        );
}
