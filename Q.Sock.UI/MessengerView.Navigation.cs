using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Q.Sock.Core;
using Q.Sock.Shared.Models;
using Q.Sock.UI.Plugins;
using static Q.Sock.UI.UiFactory;

namespace Q.Sock.UI;

public sealed partial class MessengerView
{
    private void RebuildNavigation()
    {
        if (showingLogin)
            return;
        sidebarTitle.Text = selectedGuild is { } guildId
            ? guilds.FirstOrDefault(guild => guild.Id == guildId)?.Name ?? "Пространство"
            : navigationSection;
        status.Text = $"Подключено · {DisplayName}";
        railItems.Children.Clear();
        var home = Button("Личные и групповые чаты", () => NavigateTo("Чаты"));
        home.Content = Avatar("М", 48, navigationSection == "Чаты");
        home.Classes.Add("icon");
        home.Width = home.Height = 48;
        railItems.Children.Add(home);
        foreach (var guild in guilds)
        {
            var current = guild;
            var button = Button(guild.Name, () => NavigateToGuild(current));
            button.Content = Avatar(guild.Name, guild.Id, AvatarKind.Space, 48);
            button.Width = button.Height = 48;
            button.Classes.Add("icon");
            ToolTip.SetTip(button, guild.Name);
            railItems.Children.Add(button);
        }
        railItems.Children.Add(
            IconButton("Add", "Создать пространство", () => Run(CreateGuild), compact: true)
        );
        railItems.Children.Add(new Separator());
        railItems.Children.Add(
            IconButton(
                "GroupsOutlined",
                "Пространства",
                () => NavigateTo("Пространства"),
                compact: true
            )
        );
        railItems.Children.Add(
            IconButton("CallOutlined", "Звонки", () => NavigateTo("Звонки"), compact: true)
        );
        railItems.Children.Add(
            IconButton("PersonOutlined", "Профиль", () => NavigateTo("Профиль"), compact: true)
        );
        foreach (var action in PluginActions(ClientUiLocation.Sidebar))
            railItems.Children.Add(
                IconButton(action.Icon, action.Title, () => Run(action.Execute), compact: true)
            );
        BuildBottomNavigation();
        navigation.Children.Clear();
        searchSurface!.IsVisible = navigationSection is "Чаты" or "Пространства";
        if (accountRepository is IAccountPersistenceStatus { SessionOnly: true })
            navigation.Children.Add(Text("Временный сеанс · вход не сохранён", 11, "muted"));
        var search = navigationSearch.Text?.Trim() ?? "";
        if (navigationSection == "Профиль")
        {
            BuildProfileNavigation();
            return;
        }
        if (navigationSection == "Звонки")
        {
            BuildCallsNavigation();
            return;
        }
        if (navigationSection == "Пространства" && selectedGuild is null)
        {
            foreach (
                var guild in guilds.Where(guild =>
                    guild.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                )
            )
            {
                var current = guild;
                navigation.Children.Add(
                    NavigationCard(
                        guild.Name,
                        guild.Managed ? "Управляется интеграцией" : "Пространство",
                        () => NavigateToGuild(current),
                        guild.Name,
                        guild.Id,
                        AvatarKind.Space
                    )
                );
            }
            if (navigation.Children.Count == 0)
                navigation.Children.Add(
                    EmptyList(
                        "Пространств пока нет",
                        "Создайте пространство с текстовыми каналами и голосовыми комнатами.",
                        "Создать пространство",
                        () => Run(CreateGuild)
                    )
                );
            return;
        }
        if (selectedGuild is not null)
        {
            var guild = guilds.FirstOrDefault(item => item.Id == selectedGuild);
            navigation.Children.Add(
                Text(
                    guild?.Managed == true ? ManagedDescription() : "Каналы и комнаты пространства",
                    12,
                    "muted"
                )
            );
            navigation.Children.Add(new Separator { Margin = new Thickness(0, 8) });
        }
        var banner = availableCalls.FirstOrDefault(call =>
            call.Status == "live" && call.StartsAtUtc is not null
        );
        if (banner is not null && selectedGuild is null)
            navigation.Children.Add(LessonCard(banner));
        var items = chats
            .Where(chat =>
                chat.GuildId == selectedGuild
                && (
                    chat.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                    || chat.LastMessagePreview.Contains(
                        search,
                        StringComparison.CurrentCultureIgnoreCase
                    )
                )
            )
            .OrderByDescending(chat => chat.LastMessageAt)
            .ThenBy(chat => chat.Name)
            .ToArray();
        foreach (var chat in items)
            navigation.Children.Add(ChatRow(chat));
        if (items.Length == 0)
            navigation.Children.Add(
                EmptyList(
                    search.Length > 0 ? "Ничего не найдено" : "Здесь пока тихо",
                    search.Length > 0
                        ? "Попробуйте другое название или найдите человека."
                        : "Создайте чат и начните общение.",
                    search.Length > 0 ? "Найти человека" : "Новый чат",
                    () => Run(search.Length > 0 ? ShowContacts : CreateChat)
                )
            );
        if (selectedGuild is not null)
        {
            navigation.Children.Add(
                IconButton("AddComment", "Чат или канал", () => Run(CreateChat))
            );
            foreach (var action in PluginActions(ClientUiLocation.Workspace))
                navigation.Children.Add(
                    IconButton(action.Icon, action.Title, () => Run(action.Execute))
                );
        }
    }

    private Task NavigateTo(string section)
    {
        navigationSection = section;
        selectedGuild = null;
        selectedChat = null;
        showConversation = false;
        attachments.Clear();
        navigationSearch.Text = "";
        ShowEmptyConversation(
            section == "Профиль" ? DisplayName : "Выберите чат",
            "Личные чаты, группы и пространства."
        );
        RebuildNavigation();
        UpdateLayoutMode();
        return Task.CompletedTask;
    }

    private Task NavigateToGuild(GuildInfo guild)
    {
        navigationSection = "Пространства";
        selectedGuild = guild.Id;
        selectedChat = null;
        showConversation = false;
        attachments.Clear();
        navigationSearch.Text = "";
        ShowEmptyConversation(guild.Name, "Выберите текстовый канал или голосовую комнату.");
        RebuildNavigation();
        UpdateLayoutMode();
        return Task.CompletedTask;
    }

    private void BuildBottomNavigation()
    {
        bottomNavigation.Children.Clear();
        var sections = new[]
        {
            ("Чаты", "ChatBubbleOutlineOutlined"),
            ("Пространства", "GroupsOutlined"),
            ("Звонки", "CallOutlined"),
            ("Профиль", "SettingsOutlined"),
        };
        for (var index = 0; index < sections.Length; index++)
        {
            var (name, icon) = sections[index];
            var active = navigationSection == name;
            var content = Column(
                new MaterialIcon(icon, 24) { HorizontalAlignment = HorizontalAlignment.Center },
                Text(name, 11, active ? "text" : "muted", active)
            );
            content.Spacing = 4;
            content.HorizontalAlignment = HorizontalAlignment.Center;
            var button = Button(name, () => NavigateTo(name));
            button.Content = content;
            button.Classes.Add("tab");
            button.MinHeight = 68;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.Padding = new Thickness(4, 8);
            Grid.SetColumn(button, index);
            bottomNavigation.Children.Add(button);
        }
    }

    private Button ChatRow(ChatInfo chat)
    {
        if (selectedGuild is not null)
            return ChannelRow(chat);
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("42,*,52"),
            ColumnSpacing = 12,
        };
        grid.Children.Add(Avatar(chat.Name, chat.Id, AvatarKind.Chat));
        var preview = chat.LastMessagePreview.Replace('\n', ' ').Replace('\r', ' ');
        if (preview.Length == 0)
            preview =
                chat.Type == "room" ? "Голосовая комната"
                : chat.Managed ? "Канал"
                : "Начните общение";
        var names = Column(Ellipsis(chat.Name, 14, strong: true), Ellipsis(preview, 12, "muted"));
        names.Spacing = 3;
        names.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);
        var details = new StackPanel
        {
            Spacing = 5,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        details.Children.Add(Text(ChatTime(chat.LastMessageAt), 11, "muted"));
        if (chat.UnreadCount > 0)
        {
            var badge = Surface(
                Text(chat.UnreadCount > 99 ? "99+" : chat.UnreadCount.ToString(), 11),
                "accent",
                new Thickness(6, 1),
                10
            );
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            details.Children.Add(badge);
        }
        Grid.SetColumn(details, 2);
        grid.Children.Add(details);
        var button = Button(chat.Name, () => Run(() => SelectChat(chat)));
        button.Content = grid;
        button.Classes.Add("nav");
        if (selectedChat?.Id == chat.Id)
            button.Classes.Add("selected");
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTip.SetTip(button, chat.Name);
        return button;
    }

    private Button ChannelRow(ChatInfo chat)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("24,*,Auto"),
            ColumnSpacing = 10,
        };
        grid.Children.Add(
            new MaterialIcon(chat.Type == "room" ? "VolumeUpOutlined" : "Tag", 22)
            {
                VerticalAlignment = VerticalAlignment.Center,
            }
        );
        var name = Ellipsis(chat.Name, 14, "secondary");
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        if (chat.UnreadCount > 0)
        {
            var badge = Surface(
                Text(chat.UnreadCount > 99 ? "99+" : chat.UnreadCount.ToString(), 11),
                "accent",
                new Thickness(6, 1),
                10
            );
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }
        var button = Button(chat.Name, () => Run(() => SelectChat(chat)));
        button.Content = grid;
        button.Classes.Add("nav");
        button.MinHeight = 48;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (selectedChat?.Id == chat.Id)
            button.Classes.Add("selected");
        return button;
    }

    private static string ChatTime(DateTimeOffset? value)
    {
        if (value is null)
            return "";
        var local = value.Value.LocalDateTime;
        if (local.Date == DateTime.Today)
            return local.ToString("HH:mm");
        if (local.Date == DateTime.Today.AddDays(-1))
            return "Вчера";
        return local.ToString("dd.MM");
    }

    private static Button NavigationCard(
        string title,
        string subtitle,
        Func<Task> action,
        string initial,
        Guid identity,
        AvatarKind kind
    )
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("42,*"),
            ColumnSpacing = 12,
        };
        grid.Children.Add(Avatar(initial, identity, kind));
        var copy = Column(Text(title, 14, strong: true), Text(subtitle, 12, "muted"));
        copy.Spacing = 4;
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        var button = Button(title, action);
        button.Content = grid;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Classes.Add("nav");
        return button;
    }

    private static Border EmptyList(
        string title,
        string subtitle,
        string actionTitle,
        Func<Task> action
    ) =>
        Card(
            Column(
                Text(title, 16, strong: true),
                Text(subtitle, 12, "muted"),
                Primary(actionTitle, action)
            )
        );

    private Border LessonCard(CallInfo call)
    {
        var chat = chats.FirstOrDefault(item => item.Id == call.ChatId);
        var card = Card(
            Column(
                Text(call.StartsAtUtc is null ? "Идёт звонок" : "Звонок начался", 16, strong: true),
                Text(call.Title, 13, "secondary"),
                Primary("Перейти к звонку", () => Run(() => OpenCall(call.Id)))
            )
        );
        card.Margin = new Thickness(0, 8);
        return card;
    }

    private void BuildProfileNavigation()
    {
        navigation.Children.Add(
            IconButton("HelpOutlined", "Справка Q.Sock", ShowDocumentation)
        );
        navigation.Children.Add(
            Card(
                Column(
                    Avatar(DisplayName, profile!.Id, AvatarKind.User, 58),
                    Text(DisplayName, 18, strong: true),
                    Text(client?.Server.Host ?? "", 12, "muted")
                )
            )
        );
        navigation.Children.Add(
            IconButton("Devices", "Устройства и сессии", () => Run(ShowSettings))
        );
        navigation.Children.Add(
            IconButton("PaletteOutlined", "Оформление", () => Run(ShowThemeEditor))
        );
        navigation.Children.Add(
            IconButton("PeopleOutlined", "Люди и друзья", () => Run(ShowContacts))
        );
        navigation.Children.Add(
            IconButton("ExtensionOutlined", "Модули интерфейса", () => Run(ShowClientPlugins))
        );
        navigation.Children.Add(
            IconButton("SettingsOutlined", "Настройки и аккаунты", () => Run(ShowSettings))
        );
        if (profile?.IsAdmin == true)
            navigation.Children.Add(
                IconButton(
                    "AdminPanelSettingsOutlined",
                    "Управление сервером",
                    () => Run(ShowAdmin)
                )
            );
    }

    private void BuildCallsNavigation()
    {
        if (availableCalls.Length == 0)
            navigation.Children.Add(
                EmptyList(
                    "Активных звонков нет",
                    "Начните звонок из личного или группового чата либо войдите в голосовую комнату.",
                    "Открыть чаты",
                    () => NavigateTo("Чаты")
                )
            );
        foreach (var call in availableCalls.OrderBy(call => call.StartsAtUtc))
        {
            var current = call;
            var schedule =
                call.Status == "live"
                    ? "В эфире"
                    : $"{call.StartsAtUtc?.ToLocalTime():dd.MM.yyyy · HH:mm}";
            navigation.Children.Add(
                NavigationCard(
                    call.Title,
                    schedule,
                    () => Run(() => OpenCall(current.Id)),
                    call.Title,
                    call.ChatId,
                    AvatarKind.Chat
                )
            );
        }
    }
}
