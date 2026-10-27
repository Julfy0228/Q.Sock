using Avalonia;
using Avalonia.Browser;

namespace Q.Sock.Browser;

internal static class Program
{
    private static Task Main(string[] args) =>
        AppBuilder.Configure<BrowserApplication>().WithInterFont().StartBrowserAppAsync("out");
}
