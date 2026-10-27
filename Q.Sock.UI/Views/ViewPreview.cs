using Avalonia.Controls;
using Avalonia.Themes.Fluent;
using Q.Sock.Core;

namespace Q.Sock.UI.Views;

internal static class ViewPreview
{
    public static void Configure(UserControl view)
    {
        if (!Design.IsDesignMode)
            return;
        view.Styles.Add(new FluentTheme());
        view.Styles.Add(new MessengerStyles());
        MessengerStyles.ApplyPalette(view, new MessengerTheme());
    }
}
