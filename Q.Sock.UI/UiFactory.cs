using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace Q.Sock.UI;

public static class UiFactory
{
    public static TextBlock Text(string value, double size = 14) =>
        Text(value, size, "text", false);

    public static TextBlock Text(string value, double size, bool strong) =>
        Text(value, size, "text", strong);

    public static TextBlock Text(string value, double size, string color, bool strong = false)
    {
        var text = new TextBlock
        {
            Text = value,
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal,
            LineHeight = size * 1.4,
        };
        text.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(color));
        return text;
    }

    public static Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label, MinHeight = 40 };
        AutomationProperties.SetName(button, label);
        button.Click += async (_, _) => await action();
        return button;
    }

    public static Button Primary(string label, Func<Task> action)
    {
        var button = Button(label, action);
        button.Classes.Add("primary");
        return button;
    }

    public static Button Danger(string label, Func<Task> action)
    {
        var button = Button(label, action);
        button.Classes.Add("danger");
        return button;
    }

    public static Button IconButton(
        string icon,
        string label,
        Func<Task> action,
        bool compact = false
    )
    {
        var button = Button(label, action);
        button.Classes.Add(compact ? "icon" : "secondary");
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(AppIcons.Create(icon));
        if (!compact)
            content.Children.Add(Text(label));
        button.Content = content;
        ToolTip.SetTip(button, label);
        return button;
    }

    public static Border Avatar(string name, double size = 42, bool accent = true)
    {
        var avatar = AvatarFrame(name, size);
        avatar.Bind(Border.BackgroundProperty, new DynamicResourceExtension(accent ? "accent" : "selection"));
        return avatar;
    }

    private static Border AvatarFrame(string name, double size)
    {
        var initial = string.IsNullOrWhiteSpace(name)
            ? "?"
            : System.Globalization.StringInfo.GetNextTextElement(name.Trim()).ToUpperInvariant();
        return new Border
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(size / 2),
            Child = new TextBlock
            {
                Text = initial,
                FontSize = size >= 40 ? 16 : 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    public static Border Avatar(string name, Guid id, Q.Sock.Core.AvatarKind kind, double size = 42)
    {
        var avatar = AvatarFrame(name, size);
        avatar.Background = new SolidColorBrush(
            Color.Parse(Q.Sock.Core.AvatarColors.For(id, kind))
        );
        return avatar;
    }

    public static Grid Field(string label, Control input)
    {
        var grid = new Grid();
        input.Margin = new Thickness(0, 8, 0, 0);
        input.HorizontalAlignment = HorizontalAlignment.Stretch;
        grid.Children.Add(input);
        var caption = Surface(Text(label, 12, "secondary"), "background", new Thickness(4, 0));
        caption.Margin = new Thickness(12, 0, 0, 0);
        caption.HorizontalAlignment = HorizontalAlignment.Left;
        caption.VerticalAlignment = VerticalAlignment.Top;
        caption.IsHitTestVisible = false;
        grid.Children.Add(caption);
        return grid;
    }

    public static StackPanel Column(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (var child in children)
            panel.Children.Add(child);
        return panel;
    }

    public static StackPanel Row(params Control[] children)
    {
        var panel = Column(children);
        panel.Orientation = Orientation.Horizontal;
        return panel;
    }

    public static TextBox Input(string placeholder, string value = "")
    {
        var input = new TextBox
        {
            PlaceholderText = placeholder,
            Text = value,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(input, placeholder);
        return input;
    }

    public static Border Surface(
        Control child,
        string color,
        Thickness padding = default,
        double radius = 0
    )
    {
        var surface = new Border
        {
            Child = child,
            Padding = padding,
            CornerRadius = new CornerRadius(radius),
        };
        surface.Bind(Border.BackgroundProperty, new DynamicResourceExtension(color));
        return surface;
    }

    public static Border Card(Control child) => Surface(child, "panel", new Thickness(16), 12);

    public static TextBlock Ellipsis(
        string value,
        double size = 14,
        string color = "text",
        bool strong = false
    )
    {
        var text = Text(value, size, color, strong);
        text.TextWrapping = TextWrapping.NoWrap;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        return text;
    }
}
