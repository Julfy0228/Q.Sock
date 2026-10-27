using Avalonia.Controls;
using Q.Sock.Client.Services;
using Q.Sock.Core;
using Q.Sock.Core.Plugins;
using Q.Sock.UI;

namespace Q.Sock.Client;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var profile = Environment.GetEnvironmentVariable("QSOCK_PROFILE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(profile))
        {
            Content = new MessengerView(new SessionFallbackAccountRepository(new AccountStore()));
            return;
        }
        profile = Path.GetFullPath(profile);
        Content = new MessengerView(
            new SessionFallbackAccountRepository(new AccountStore(profile)),
            preferences: new FileClientPreferences(profile),
            pluginCache: new FilePluginCache(Path.Combine(profile, "plugins"))
        );
    }
}
