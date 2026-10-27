using System.Runtime.InteropServices.JavaScript;
using Q.Sock.Core;

namespace Q.Sock.Browser;

public sealed partial class BrowserPreferences : IClientPreferences
{
    [JSImport("readPreference", "qsock")]
    private static partial string ReadPreference(string key);

    [JSImport("writePreference", "qsock")]
    private static partial void WritePreference(string key, string value);

    public Task<string?> Read(string key)
    {
        var value = ReadPreference(key);
        return Task.FromResult<string?>(value.Length == 0 ? null : value);
    }

    public Task Write(string key, string value)
    {
        WritePreference(key, value);
        return Task.CompletedTask;
    }
}
