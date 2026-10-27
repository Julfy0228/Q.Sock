using System.Text.Json;
using Q.Sock.Core;
using Q.Sock.Core.Plugins;

namespace Q.Sock.UI.Plugins;

public sealed class PublisherTrustStore(IClientPreferences preferences)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<bool> IsTrusted(Uri server, PluginPackage package)
    {
        var entries = await ReadEntries();
        return entries.Contains(TrustKey(server, package));
    }

    public async Task Trust(Uri server, PluginPackage package)
    {
        await gate.WaitAsync();
        try
        {
            var entries = await ReadEntries();
            entries.Add(TrustKey(server, package));
            await preferences.Write("plugin-trust", JsonSerializer.Serialize(entries));
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<HashSet<string>> ReadEntries()
    {
        var json = await preferences.Read("plugin-trust");
        return json is null ? [] : JsonSerializer.Deserialize<HashSet<string>>(json) ?? [];
    }

    private static string TrustKey(Uri server, PluginPackage package) =>
        string.Join(
            "|",
            server.GetLeftPart(UriPartial.Authority),
            package.Id,
            package.PublisherFingerprint()
        );
}
