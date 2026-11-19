using System.Text.Json;
using Avalonia.Controls;
using Q.Sock.Shared.Models;
using Q.Sock.UI.Plugins;
using static Q.Sock.UI.UiFactory;

namespace Q.Sock.UI;

public sealed partial class MessengerView
{
    private async Task ShowAdmin()
    {
        var policy = await client!.Get<ServerPolicy>("api/admin/policy");
        var registration = new CheckBox
        {
            Content = "Разрешить регистрацию",
            IsChecked = policy.AllowRegistration,
        };
        var uploadLimit = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 100,
            Value = policy.MaxUploadMb,
            FormatString = "0",
        };
        var form = Column(
            Text("Правила сервера", 20),
            registration,
            Text("Размер файла, МБ"),
            uploadLimit,
            Button(
                "Сохранить правила",
                () =>
                    Run(async () =>
                    {
                        policy.AllowRegistration = registration.IsChecked == true;
                        policy.MaxUploadMb = (int)(uploadLimit.Value ?? 25);
                        await client.Mutate("api/admin/policy", HttpMethod.Put, policy);
                        status.Text = "Правила сервера сохранены";
                    })
            ),
            Button("Скачать резервную копию БД", () => Run(DownloadBackup)),
            Text("Заявки на удаление пространств", 20)
        );
        foreach (var action in PluginActions(ClientUiLocation.ServerSettings))
            form.Children.Add(IconButton(action.Icon, action.Title, () => Run(action.Execute)));
        var requests = await client.Get<JsonElement[]>("api/admin/deletions");
        foreach (var request in requests)
        {
            var id = request.GetProperty("id").GetGuid();
            var item = Column(Text(request.GetProperty("name").GetString() ?? "Пространство"));
            item.Children.Add(
                Danger(
                    "Подтвердить удаление",
                    () =>
                        Run(async () =>
                        {
                            await client.Mutate($"api/admin/deletions/{id}/true", HttpMethod.Post);
                            await ShowAdmin();
                        })
                )
            );
            item.Children.Add(
                Button(
                    "Отклонить заявку",
                    () =>
                        Run(async () =>
                        {
                            await client.Mutate($"api/admin/deletions/{id}/false", HttpMethod.Post);
                            await ShowAdmin();
                        })
                )
            );
            form.Children.Add(Card(item));
        }
        if (requests.Length == 0)
            form.Children.Add(Text("Заявок нет"));
        form.Children.Add(Button("Журнал действий", () => Run(ShowAudit)));
        await ShowDialog("Управление сервером", form);
    }

    private async Task ShowAudit()
    {
        var entries = await client!.Get<JsonElement[]>("api/admin/audit");
        var list = new StackPanel { Spacing = 10 };
        foreach (var entry in entries)
            list.Children.Add(
                Card(
                    Column(
                        Text(entry.GetProperty("action").GetString() ?? ""),
                        Text(
                            $"{entry.GetProperty("atUtc").GetDateTime().ToLocalTime():g} · {entry.GetProperty("target").GetString()}",
                            12
                        )
                    )
                )
            );
        if (entries.Length == 0)
            list.Children.Add(Text("Журнал пока пуст"));
        await ShowDialog("Журнал действий", list);
    }

    private async Task DownloadBackup()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return;
        var file = await top.StorageProvider.SaveFilePickerAsync(
            new() { SuggestedFileName = $"qsock-{DateTime.Now:yyyyMMdd-HHmm}.db" }
        );
        if (file is null)
            return;
        var bytes = await client!.Download("api/admin/backup", HttpMethod.Post);
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(bytes);
        status.Text = "Резервная копия сохранена";
    }
}
