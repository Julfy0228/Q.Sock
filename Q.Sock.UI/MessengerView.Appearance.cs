using Avalonia.Controls;
using Q.Sock.Core;
using Q.Sock.UI.ViewModels;
using Q.Sock.UI.Views;

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

    private Task ShowThemeEditor()
    {
        var model = new AppearanceViewModel(
            currentTheme,
            proposed => Run(() => SaveEditedTheme(proposed)),
            () => Run(() => SaveEditedTheme(new MessengerTheme()))
        );
        return ShowDialog("Оформление", new AppearanceView { DataContext = model });
    }

    private async Task SaveEditedTheme(MessengerTheme proposed)
    {
        currentTheme = MessengerTheme.Parse(proposed.Export());
        ApplyTheme();
        await preferences.Write("theme", currentTheme.Export());
        CloseDialog();
    }
}
