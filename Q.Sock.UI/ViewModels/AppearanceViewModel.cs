using System.Collections.ObjectModel;
using Q.Sock.Core;

namespace Q.Sock.UI.ViewModels;

public sealed class AppearanceViewModel : ViewModel
{
    private string name;

    public AppearanceViewModel(
        MessengerTheme theme,
        Func<MessengerTheme, Task> apply,
        Func<Task> reset
    )
    {
        name = theme.Name;
        foreach (
            var (key, label) in new[]
            {
                ("background", "Фон"),
                ("panel", "Панели"),
                ("surface", "Поля и кнопки"),
                ("text", "Основной текст"),
                ("muted", "Второстепенный текст"),
                ("accent", "Акцент"),
            }
        )
            Colors.Add(new ThemeColorItem(key, label, theme.Colors[key]));
        ApplyCommand = new(() =>
            apply(
                new MessengerTheme
                {
                    Name = Name.Trim(),
                    Colors = Colors.ToDictionary(item => item.Key, item => item.Value.Trim()),
                }
            )
        );
        ResetCommand = new(reset);
    }

    public string Name
    {
        get => name;
        set => Set(ref name, value ?? "");
    }
    public ObservableCollection<ThemeColorItem> Colors { get; } = [];
    public AsyncCommand ApplyCommand { get; }
    public AsyncCommand ResetCommand { get; }
}

public sealed class ThemeColorItem(string key, string label, string value) : ViewModel
{
    private string color = value;
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Value
    {
        get => color;
        set => Set(ref color, value ?? "");
    }
}
