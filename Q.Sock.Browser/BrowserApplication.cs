using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Q.Sock.UI;

namespace Q.Sock.Browser;

public sealed class BrowserApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime lifetime)
            lifetime.MainView = new MessengerView(
                new BrowserAccountRepository(),
                BrowserStorage.Origin(),
                BrowserStorage.ConsumeLaunchCode(),
                new BrowserCallPlatform(),
                null,
                new BrowserPreferences(),
                new BrowserQrScanner(),
                pluginCache: new BrowserPluginCache(),
                pluginSignatures: new BrowserPluginSignatureVerifier()
            );
        base.OnFrameworkInitializationCompleted();
    }
}
