using System.Net.Http;
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
                var result = await client!.Post<JsonElement>("api/guilds", new { name });
                selectedGuild = result.GetProperty("id").GetGuid();
                await RefreshWorkspace();
            }
        );

    private async Task CreateChat()
    {
        var name = Input("Название");
        var types = selectedGuild is null
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
        if (selectedGuild is null)
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
                                GuildId = selectedGuild,
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
        await ShowDialog("Новый чат или канал", form);
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

    private async Task ShowMembers()
    {
        if (selectedChat is null || client is null)
            return;
        var members = await client.Get<MemberInfo[]>($"api/chats/{selectedChat.Id}/members");
        var list = new StackPanel { Spacing = 10 };
        if (
            selectedChat.CanManage
            && !selectedChat.Managed
            && selectedChat.GuildId is null
            && selectedChat.Type == "group"
        )
            list.Children.Add(
                Button(
                    "Добавить участника",
                    () =>
                        Run(() =>
                            PickUser(async person =>
                            {
                                await client.Mutate(
                                    $"api/chats/{selectedChat.Id}/members/{person.Id}",
                                    HttpMethod.Put,
                                    new MemberUpdate()
                                );
                                await ShowMembers();
                            })
                        )
                )
            );
        foreach (var member in members)
        {
            var item = Column(
                Text(member.DisplayName == "" ? member.UserName : member.DisplayName),
                Text($"{RoleLabel(member.Role)} · {string.Join(", ", member.Groups)}", 11)
            );
            if (
                selectedChat.Type != "direct"
                && selectedChat.CanManage
                && member.UserId != profile?.Id
                && member.Role != "owner"
            )
            {
                var roles = new[]
                {
                    new Choice("member", "Участник"),
                    new Choice("moderator", "Модератор"),
                };
                var role = new ComboBox
                {
                    ItemsSource = roles,
                    SelectedItem =
                        roles.FirstOrDefault(item => item.Value == member.Role) ?? roles[0],
                };
                if (!selectedChat.Managed)
                    item.Children.Add(role);
                var write = new CheckBox { Content = "Сообщения", IsChecked = member.CanWrite };
                var speak = new CheckBox { Content = "Голос", IsChecked = member.CanSpeak };
                var connect = new CheckBox
                {
                    Content = "Подключение",
                    IsChecked = member.CanConnect,
                };
                var share = new CheckBox
                {
                    Content = "Демонстрация экрана",
                    IsChecked = member.CanShare,
                };
                item.Children.Add(Column(write, speak, connect, share));
                item.Children.Add(
                    Button(
                        "Применить ограничения",
                        () =>
                            Run(async () =>
                            {
                                await client.Mutate(
                                    $"api/chats/{selectedChat.Id}/members/{member.UserId}",
                                    HttpMethod.Put,
                                    new MemberUpdate
                                    {
                                        Role = selectedChat.Managed
                                            ? member.Role
                                            : (role.SelectedItem as Choice)?.Value ?? "member",
                                        CanWrite = write.IsChecked == true,
                                        CanSpeak = speak.IsChecked == true,
                                        CanConnect = connect.IsChecked == true,
                                        CanShare = share.IsChecked == true,
                                    }
                                );
                                await ShowMembers();
                            })
                    )
                );
                if (!selectedChat.Managed && selectedChat.GuildId is null)
                    item.Children.Add(
                        Button(
                            "Исключить из чата",
                            () =>
                                Run(async () =>
                                {
                                    await client.Mutate(
                                        $"api/chats/{selectedChat.Id}/members/{member.UserId}",
                                        HttpMethod.Delete
                                    );
                                    await ShowMembers();
                                })
                        )
                    );
            }
            list.Children.Add(Card(item));
        }
        await ShowDialog("Участники", list);
    }

    private async Task StartCall()
    {
        if (selectedChat is null || client is null)
            return;
        if (selectedChat.Type is "direct" or "room")
        {
            var call = await client.Post<CallInfo>(
                "api/calls",
                new CallRequest
                {
                    ChatId = selectedChat.Id,
                    RingAll = selectedChat.Type == "direct",
                }
            );
            await OpenCall(call.Id);
            return;
        }
        var members = await client.Get<MemberInfo[]>($"api/chats/{selectedChat.Id}/members");
        var ringAll = new CheckBox
        {
            Content = "Позвонить всем, кроме отмеченных",
            IsChecked = true,
        };
        var selected = new HashSet<Guid>();
        var form = Column(
            ringAll,
            Text("Отключите переключатель, чтобы позвонить только отмеченным.", 12)
        );
        foreach (var member in members.Where(member => member.UserId != profile?.Id))
        {
            var check = new CheckBox
            {
                Content = member.DisplayName == "" ? member.UserName : member.DisplayName,
            };
            check.IsCheckedChanged += (_, _) =>
            {
                if (check.IsChecked == true)
                    selected.Add(member.UserId);
                else
                    selected.Remove(member.UserId);
            };
            form.Children.Add(check);
        }
        form.Children.Add(
            Button(
                "Начать звонок",
                () =>
                    Run(async () =>
                    {
                        var call = await client.Post<CallInfo>(
                            "api/calls",
                            new CallRequest
                            {
                                ChatId = selectedChat.Id,
                                RingAll = ringAll.IsChecked == true,
                                RingUserIds = ringAll.IsChecked == true ? [] : selected.ToArray(),
                                ExcludedUserIds =
                                    ringAll.IsChecked == true ? selected.ToArray() : [],
                            }
                        );
                        await OpenCall(call.Id);
                        CloseDialog();
                    })
            )
        );
        var calls = await client.Get<CallInfo[]>("api/calls");
        foreach (var call in calls.Where(call => call.ChatId == selectedChat.Id))
            form.Children.Add(
                Button($"Присоединиться: {call.Title}", () => Run(() => OpenCall(call.Id)))
            );
        await ShowDialog("Звонок", form);
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
