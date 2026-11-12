using Q.Sock.Core;

namespace Q.Sock.Tests.Plugins;

internal sealed class MemoryPreferences : IClientPreferences
{
    private readonly Dictionary<string, string> values = [];

    public Task<string?> Read(string key) => Task.FromResult(values.GetValueOrDefault(key));

    public Task Write(string key, string value)
    {
        values[key] = value;
        return Task.CompletedTask;
    }
}
