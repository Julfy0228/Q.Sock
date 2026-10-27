using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Q.Sock.Core;
using Q.Sock.UI;

namespace Q.Sock.Android;

public sealed class MobileApplication : Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime lifetime)
        {
            var directory = global::Android.App.Application.Context.FilesDir!.AbsolutePath;
            lifetime.MainView = new MessengerView(
                new AndroidAccountRepository(directory),
                preferences: new FileClientPreferences(directory),
                displaySettings: new AndroidDisplaySettings()
            );
        }
        base.OnFrameworkInitializationCompleted();
    }
}
