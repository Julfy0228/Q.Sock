using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using Q.Sock.Core;
using static Q.Sock.UI.UiFactory;

namespace Q.Sock.UI;

public sealed partial class MessengerView
{
    private async Task ImportQrTheme()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return;
        var files = await top.StorageProvider.OpenFilePickerAsync(
            new()
            {
                Title = "Изображение с QR-кодом темы",
                AllowMultiple = false,
                FileTypeFilter = [Avalonia.Platform.Storage.FilePickerFileTypes.ImageAll],
            }
        );
        if (files.Count == 0)
            return;
        await using var source = await files[0].OpenReadAsync();
        using var image = new MemoryStream();
        var buffer = new byte[65536];
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            if (image.Length + read > 10 * 1024 * 1024)
                throw new InvalidDataException("Изображение должно быть меньше 10 МБ.");
            await image.WriteAsync(buffer.AsMemory(0, read));
        }
        image.Position = 0;
        await ApplyImportedTheme(QrCodeReader.Decode(image));
    }

    private async Task ScanTheme()
    {
        if (await qrScanner!.Scan() is { } json)
            await ApplyImportedTheme(json);
    }

    private async Task ApplyImportedTheme(string json)
    {
        currentTheme = MessengerTheme.Parse(json);
        ApplyTheme();
        await preferences.Write("theme", currentTheme.Export());
        status.Text = $"Применена тема «{currentTheme.Name}»";
    }

    private void ConfigureThemeStyles() => Styles.Add(new MessengerStyles());

    private async Task ShowThemeEditor()
    {
        var name = Input("Название темы", currentTheme.Name);
        var fields = new Dictionary<string, TextBox>();
        var labels = new Dictionary<string, string>
        {
            ["background"] = "Фон",
            ["panel"] = "Панели",
            ["surface"] = "Поля и кнопки",
            ["text"] = "Основной текст",
            ["muted"] = "Второстепенный текст",
            ["accent"] = "Акцент",
        };
        var form = Column(name);
        foreach (var (key, label) in labels)
        {
            var input = Input("#RRGGBB", currentTheme.Colors[key]);
            fields[key] = input;
            form.Children.Add(Column(Text(label), input));
        }
        form.Children.Add(
            Button(
                "Применить",
                () =>
                    Run(async () =>
                    {
                        var proposed = new MessengerTheme
                        {
                            Name = name.Text?.Trim() ?? "",
                            Colors = fields.ToDictionary(
                                item => item.Key,
                                item => item.Value.Text?.Trim() ?? ""
                            ),
                        };
                        currentTheme = MessengerTheme.Parse(proposed.Export());
                        ApplyTheme();
                        await preferences.Write("theme", currentTheme.Export());
                        CloseDialog();
                    })
            )
        );
        form.Children.Add(
            Button(
                "Сбросить тему",
                () =>
                    Run(async () =>
                    {
                        currentTheme = new();
                        ApplyTheme();
                        await preferences.Write("theme", currentTheme.Export());
                        CloseDialog();
                    })
            )
        );
        await ShowDialog("Оформление", form);
    }
}
