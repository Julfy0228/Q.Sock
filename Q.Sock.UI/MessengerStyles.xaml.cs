using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Q.Sock.Core;

namespace Q.Sock.UI;

public sealed partial class MessengerStyles : Styles
{
    public MessengerStyles() => AvaloniaXamlLoader.Load(this);

    public static bool IsPreviousDefault(MessengerTheme theme)
    {
        var previous = new Dictionary<string, string>
        {
            ["background"] = "#171A22",
            ["panel"] = "#202430",
            ["surface"] = "#2B3040",
            ["text"] = "#ECEEF5",
            ["muted"] = "#A2A8BD",
            ["accent"] = "#8585FF",
        };
        return theme.Name == "Сумерки"
            && previous.All(pair => theme.Colors.GetValueOrDefault(pair.Key) == pair.Value);
    }

    public static void ApplyPalette(StyledElement target, MessengerTheme theme)
    {
        foreach (var (key, value) in theme.Colors)
            target.Resources[key] = Brush.Parse(value);
        var background = Color.Parse(theme.Colors["background"]);
        var text = Color.Parse(theme.Colors["text"]);
        var surface = Color.Parse(theme.Colors["surface"]);
        var roles = new Dictionary<string, Color>
        {
            ["chrome"] = Blend(background, Colors.Black, .29),
            ["popup"] = Blend(background, Colors.Black, .4),
            ["selection"] = Blend(surface, Colors.White, .04),
            ["separator"] = Blend(surface, Colors.White, .035),
            ["secondary"] = Blend(text, background, .32),
            ["message"] = Blend(text, background, .12),
            ["accentHover"] = Blend(Color.Parse(theme.Colors["accent"]), Colors.Black, .2),
            ["link"] = Blend(Color.Parse(theme.Colors["accent"]), text, .28),
            ["linkHover"] = Blend(Color.Parse(theme.Colors["accent"]), text, .48),
            ["danger"] = Color.Parse("#D9534F"),
        };
        if (theme.Colors["background"] == "#313338" && theme.Colors["surface"] == "#383A40")
        {
            roles["chrome"] = Color.Parse("#232428");
            roles["popup"] = Color.Parse("#1E1F22");
            roles["selection"] = Color.Parse("#404249");
            roles["separator"] = Color.Parse("#3F4147");
            roles["secondary"] = Color.Parse("#B5BAC1");
            roles["message"] = Color.Parse("#DBDEE1");
        }
        foreach (var (key, value) in roles)
            target.Resources[key] = new SolidColorBrush(value);
        foreach (
            var (legacy, modern) in new[]
            {
                ("AccentBrush", "accent"),
                ("AccentHoverBrush", "accentHover"),
                ("InputBackgroundBrush", "background"),
                ("ButtonBackgroundBrush", "surface"),
                ("ButtonHoverBrush", "selection"),
                ("PrimaryTextBrush", "text"),
                ("SelectionBrush", "selection"),
                ("DangerBrush", "danger"),
            }
        )
            target.Resources[legacy] = target.Resources[modern];
        foreach (
            var name in new[]
            {
                "TextControlBackground",
                "TextControlBackgroundPointerOver",
                "TextControlBackgroundFocused",
            }
        )
            target.Resources[name] = target.Resources["background"];
        target.Resources["TextControlBorderBrush"] = target.Resources["separator"];
        target.Resources["TextControlBorderBrushFocused"] = target.Resources["accent"];
        target.Resources["TextControlBorderBrushPointerOver"] = target.Resources["separator"];
        target.Resources["SystemAccentColor"] = Color.Parse(theme.Colors["accent"]);
    }

    private static Color Blend(Color first, Color second, double amount) =>
        Color.FromRgb(
            (byte)Math.Round(first.R * (1 - amount) + second.R * amount),
            (byte)Math.Round(first.G * (1 - amount) + second.G * amount),
            (byte)Math.Round(first.B * (1 - amount) + second.B * amount)
        );
}
