using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Q.Sock.Core;
using Q.Sock.Shared.Models;
using Q.Sock.Shared.Models.Auth;
using static Q.Sock.UI.UiFactory;

namespace Q.Sock.UI;

public sealed partial class MessengerView
{
    private Task CreateGuild() =>
        ShowTextForm(
            "Новое пространство",
            "Название",
            "",
            async name =>
            {
                var source = client!;
                var result = await source.Post<JsonElement>("api/guilds", new { name });
                var id = result.GetProperty("id").GetGuid();
                var updatedGuilds = await source.Get<GuildInfo[]>("api/guilds");
                var updatedChats = await source.Get<ChatInfo[]>("api/chats");
                if (client == source && updatedGuilds.FirstOrDefault(guild => guild.Id == id) is { } guild)
                {
                    guilds = updatedGuilds;
                    chats = updatedChats;
                    await NavigateToGuild(guild);
                }
            }
        );

    private async Task CreateChat()
    {
        var guildId = selectedGuild;
        var name = Input("Название");
        var types = guildId is null
            ? new[] { new Choice("direct", "Личный чат"), new Choice("group", "Групповой чат") }
            : new[]
            {
                new Choice("channel", "Текстовый канал"),
                new Choice("room", "Голосовая комната"),
            };
        var type = new ComboBox { ItemsSource = types, SelectedIndex = 0 };
        var search = Input("Найти пользователя");
        var choices = new StackPanel { Spacing = 6 };
        var selected = new HashSet<Guid>();
        var find = Button(
            "Найти",
            () =>
                Run(async () =>
                {
                    choices.Children.Clear();
                    var people = await client!.Get<Profile[]>(
                        $"api/users?search={Uri.EscapeDataString(search.Text ?? "")}"
                    );
                    foreach (var person in people.Where(person => person.Id != profile?.Id))
                    {
                        var check = new CheckBox
                        {
                            Content =
                                person.DisplayName == "" ? person.UserName : person.DisplayName,
                        };
                        check.IsCheckedChanged += (_, _) =>
                        {
                            if (check.IsChecked == true)
                                selected.Add(person.Id);
                            else
                                selected.Remove(person.Id);
                        };
                        choices.Children.Add(check);
                    }
                })
        );
        var form = Column(type, name);
        if (guildId is null)
        {
            form.Children.Add(search);
            form.Children.Add(find);
            form.Children.Add(choices);
        }
        form.Children.Add(
            Button(
                "Создать",
                () =>
                    Run(async () =>
                    {
                        var result = await client!.Post<JsonElement>(
                            "api/chats",
                            new CreateChat
                            {
                                Type = (type.SelectedItem as Choice)?.Value ?? "group",
                                Name = name.Text ?? "",
                                GuildId = guildId,
                                UserIds = selected.ToArray(),
                            }
                        );
                        await RefreshWorkspace();
                        await SelectChat(
                            chats.Single(chat => chat.Id == result.GetProperty("id").GetGuid())
                        );
                        CloseDialog();
                    })
            )
        );
        await ShowDialog(guildId is null ? "Новый чат" : "Новый канал", form);
    }

    private async Task ShowContacts()
    {
        var search = Input("Имя или @логин пользователя");
        var results = new StackPanel { Spacing = 8 };
        var friends = await client!.Get<FriendInfo[]>("api/friends");
        foreach (var friend in friends)
        {
            var person = Column(
                Text(friend.UserName),
                Text(
                    friend.Status == "accepted" ? "В друзьях"
                        : friend.Incoming ? "Входящая заявка"
                        : "Заявка отправлена",
                    11
                )
            );
            if (friend.Incoming && friend.Status == "pending")
                person.Children.Add(
                    Button(
                        "Принять",
                        () =>
                            Run(async () =>
                            {
                                await client.Mutate(
                                    $"api/friends/{friend.UserId}",
                                    HttpMethod.Post
                                );
                                await ShowContacts();
                            })
                    )
                );
            person.Children.Add(
                Button(
                    "Удалить контакт",
                    () =>
                        Run(async () =>
                        {
                            await client.Mutate($"api/friends/{friend.UserId}", HttpMethod.Delete);
                            await ShowContacts();
                        })
                )
            );
            results.Children.Add(Card(person));
        }
        var find = Button(
            "Найти",
            () =>
                Run(async () =>
                {
                    results.Children.Clear();
                    var people = await client.Get<Profile[]>(
                        $"api/users?search={Uri.EscapeDataString((search.Text ?? "").TrimStart('@'))}"
                    );
                    foreach (var person in people.Where(person => person.Id != profile?.Id))
                    {
                        results.Children.Add(
                            Card(
                                Column(
                                    Text(
                                        person.DisplayName == ""
                                            ? person.UserName
                                            : person.DisplayName
                                    ),
                                    Button(
                                        "Написать",
                                        () =>
                                            Run(async () =>
                                            {
                                                var result = await client.Post<JsonElement>(
                                                    "api/chats",
                                                    new CreateChat
                                                    {
                                                        Type = "direct",
                                                        UserIds = [person.Id],
                                                    }
                                                );
                                                selectedGuild = null;
                                                await RefreshWorkspace();
                                                await SelectChat(
                                                    chats.Single(chat =>
                                                        chat.Id
                                                        == result.GetProperty("id").GetGuid()
                                                    )
                                                );
                                                CloseDialog();
                                            })
                                    ),
                                    Button(
                                        "Добавить в друзья",
                                        () =>
                                            Run(() =>
                                                client.Mutate(
                                                    $"api/friends/{person.Id}",
                                                    HttpMethod.Post
                                                )
                                            )
                                    )
                                )
                            )
                        );
                    }
                })
        );
        await ShowDialog("Люди и друзья", Column(search, find, results));
    }

    private async Task StartCall()
    {
        if (selectedChat is not { } chat || client is not { } source)
            return;
        if (chat.Type is "direct" or "room")
        {
            var call = await source.Post<CallInfo>(
                "api/calls",
                new CallRequest { ChatId = chat.Id, RingAll = chat.Type == "direct" }
            );
            if (client == source)
                await OpenCall(call.Id);
            return;
        }

        var members = await source.Get<MemberInfo[]>($"api/chats/{chat.Id}/members");
        var calls = await source.Get<CallInfo[]>("api/calls");
        if (client != source || selectedChat?.Id != chat.Id)
            return;

        var everyone = new CheckBox { Content = "Все", IsChecked = true };
        var choices = members
            .Where(member => member.UserId != profile?.Id && member.CanConnect)
            .OrderBy(MemberName)
            .Select(member =>
            {
                var check = new CheckBox
                {
                    Content = PersonIdentity(MemberName(member), member.UserId),
                    IsChecked = true,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                };
                Avalonia.Automation.AutomationProperties.SetName(check, MemberName(member));
                return (member.UserId, Check: check);
            })
            .ToArray();
        var synchronizing = false;
        everyone.IsEnabled = choices.Length > 0;
        everyone.IsChecked = choices.Length > 0;
        everyone.IsCheckedChanged += (_, _) =>
        {
            if (synchronizing)
                return;
            synchronizing = true;
            foreach (var (_, choice) in choices)
                choice.IsChecked = everyone.IsChecked == true;
            synchronizing = false;
        };
        var form = Column(everyone);
        foreach (var (_, choice) in choices)
        {
            choice.IsCheckedChanged += (_, _) =>
            {
                if (synchronizing)
                    return;
                synchronizing = true;
                var count = choices.Count(item => item.Check.IsChecked == true);
                everyone.IsChecked = count == 0 ? false : count == choices.Length ? true : null;
                synchronizing = false;
            };
            form.Children.Add(choice);
        }
        form.Children.Add(
            Primary("Начать звонок", () => Run(async () =>
            {
                if (client != source || selectedChat?.Id != chat.Id)
                    return;
                var call = await source.Post<CallInfo>(
                    "api/calls",
                    new CallRequest
                    {
                        ChatId = chat.Id,
                        RingAll = false,
                        RingUserIds = choices
                            .Where(item => item.Check.IsChecked == true)
                            .Select(item => item.UserId)
                            .ToArray(),
                    }
                );
                CloseDialog();
                await OpenCall(call.Id);
            }))
        );
        foreach (var call in calls.Where(call => call.ChatId == chat.Id))
            form.Children.Add(
                Button($"Присоединиться: {call.Title}", () => Run(() => OpenCall(call.Id)))
            );
        await ShowDialog("Кому позвонить", form);
    }

    private async Task OpenCall(Guid callId)
    {
        if (callPlatform is not null)
        {
            await ShowCall(callId);
            return;
        }
        var bridge = await client!.Post<JsonElement>($"api/auth/bridge?callId={callId}");
        var address = new Uri(
            client.Server,
            $"avalonia/index.html#launch={bridge.GetProperty("code").GetString()}"
        );
        if (TopLevel.GetTopLevel(this) is { } top)
            await top.Launcher.LaunchUriAsync(address);
    }

    private MessengerTheme currentTheme = new();

    private async Task ImportTheme()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return;
        var files = await top.StorageProvider.OpenFilePickerAsync(
            new() { Title = "Импорт темы", AllowMultiple = false }
        );
        if (files.Count == 0)
            return;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        currentTheme = MessengerTheme.Parse(await reader.ReadToEndAsync());
        ApplyTheme();
        await preferences.Write("theme", currentTheme.Export());
    }

    private void ApplyTheme()
    {
        Background = Brush.Parse(currentTheme.Colors["background"]);
        MessengerStyles.ApplyPalette(this, currentTheme);
        activeCall?.ApplyTheme(currentTheme);
    }

    private async Task ExportTheme(bool qr)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return;
        var file = await top.StorageProvider.SaveFilePickerAsync(
            new() { SuggestedFileName = qr ? "qsock-theme.png" : "qsock-theme.json" }
        );
        if (file is null)
            return;
        var bytes = qr
            ? currentTheme.QrPng()
            : System.Text.Encoding.UTF8.GetBytes(currentTheme.Export());
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(bytes);
    }
}
