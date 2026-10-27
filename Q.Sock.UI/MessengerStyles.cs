using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Q.Sock.Core;

namespace Q.Sock.UI;

public sealed class MessengerStyles : Styles
{
    private static DynamicResourceExtension Resource(string name) => new(name);

    public MessengerStyles()
    {
        Add(
            new Style(s => s.OfType<Button>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, Resource("surface")),
                    new Setter(TemplatedControl.ForegroundProperty, Resource("text")),
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                    new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(8)),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(16, 10)),
                    new Setter(TemplatedControl.FontSizeProperty, 14d),
                    new Setter(Layoutable.MinHeightProperty, 40d),
                },
            }
        );
        Add(
            new Style(s => s.OfType<TextBox>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, Resource("background")),
                    new Setter(TemplatedControl.ForegroundProperty, Resource("text")),
                    new Setter(TemplatedControl.BorderBrushProperty, Resource("separator")),
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                    new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(14, 12)),
                    new Setter(TemplatedControl.FontSizeProperty, 14d),
                    new Setter(Layoutable.MinHeightProperty, 56d),
                },
            }
        );
        AddField<ComboBox>();
        AddField<CalendarDatePicker>();
        AddField<TimePicker>();
        foreach (var name in new[] { "quiet", "nav", "tab", "icon" })
        {
            Add(
                new Style(s => s.OfType<Button>().Class(name))
                {
                    Setters =
                    {
                        new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                        new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                    },
                }
            );
            Add(
                new Style(s =>
                    s.OfType<Button>()
                        .Class(name)
                        .Class(":pointerover")
                        .Template()
                        .OfType<ContentPresenter>()
                )
                {
                    Setters =
                    {
                        new Setter(ContentPresenter.BackgroundProperty, Resource("selection")),
                    },
                }
            );
        }
        Add(
            new Style(s => s.OfType<Button>().Class("icon"))
            {
                Setters =
                {
                    new Setter(Layoutable.WidthProperty, 40d),
                    new Setter(Layoutable.HeightProperty, 40d),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                    new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(20)),
                    new Setter(
                        ContentControl.HorizontalContentAlignmentProperty,
                        Avalonia.Layout.HorizontalAlignment.Center
                    ),
                    new Setter(
                        ContentControl.VerticalContentAlignmentProperty,
                        Avalonia.Layout.VerticalAlignment.Center
                    ),
                },
            }
        );
        Add(
            new Style(s => s.OfType<Button>().Class("nav"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(12)),
                    new Setter(
                        ContentControl.HorizontalContentAlignmentProperty,
                        Avalonia.Layout.HorizontalAlignment.Stretch
                    ),
                    new Setter(Layoutable.MinHeightProperty, 64d),
                },
            }
        );
        Add(
            new Style(s => s.OfType<Button>().Class("selected"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, Resource("selection")),
                },
            }
        );
        Add(
            new Style(s => s.OfType<Button>().Class("primary"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, Resource("accent")),
                    new Setter(TemplatedControl.ForegroundProperty, Brushes.White),
                },
            }
        );
        Add(
            new Style(s =>
                s.OfType<Button>()
                    .Class("primary")
                    .Class(":pointerover")
                    .Template()
                    .OfType<ContentPresenter>()
            )
            {
                Setters =
                {
                    new Setter(ContentPresenter.BackgroundProperty, Resource("accentHover")),
                },
            }
        );
        Add(
            new Style(s => s.OfType<Button>().Class("danger"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, Resource("danger")),
                    new Setter(TemplatedControl.ForegroundProperty, Brushes.White),
                },
            }
        );
        Add(
            new Style(s => s.OfType<TextBox>().Class("composer"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(2, 9)),
                    new Setter(Layoutable.MinHeightProperty, 40d),
                },
            }
        );
        Add(
            new Style(s => s.OfType<TextBox>().Class("search"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(4, 10)),
                    new Setter(Layoutable.MinHeightProperty, 44d),
                    new Setter(TemplatedControl.FontSizeProperty, 13d),
                },
            }
        );
    }

    private void AddField<T>()
        where T : TemplatedControl
    {
        Add(
            new Style(selector => selector.OfType<T>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, Resource("background")),
                    new Setter(TemplatedControl.ForegroundProperty, Resource("text")),
                    new Setter(TemplatedControl.BorderBrushProperty, Resource("separator")),
                    new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
                    new Setter(Layoutable.MinHeightProperty, 56d),
                    new Setter(Layoutable.MinWidthProperty, 0d),
                },
            }
        );
    }

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
