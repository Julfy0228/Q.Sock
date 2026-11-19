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
    private TextBlock? conversationHeaderTitle;
    private Button? conversationBack;
    private Button? sendMessageButton;
    private Button? attachFileButton;
    private Border? conversationLesson;
    private readonly StackPanel attachmentList = new() { Spacing = 8 };
    private readonly TextBlock composerNotice = Text("", 12, "muted");

    private void BuildConversation(ChatInfo chat)
    {
        conversation.Children.Clear();
        Detach(messageScroll);
        Detach(messageInput);
        Detach(attachmentList);
        Detach(composerNotice);
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(16, 12),
        };
        conversationBack = IconButton(
            "ArrowBack",
            "Назад к чатам",
            () =>
            {
                showConversation = false;
                UpdateLayoutMode();
                return Task.CompletedTask;
            },
            compact: true
        );
        toolbar.Children.Add(conversationBack);
        conversationHeaderTitle = Ellipsis(
            chat.Type == "channel" ? "# " + chat.Name : chat.Name,
            24,
            strong: true
        );
        conversationHeaderTitle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(conversationHeaderTitle, 1);
        toolbar.Children.Add(conversationHeaderTitle);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (chat.Type != "room")
            actions.Children.Add(IconButton("Call", "Звонок", () => Run(StartCall), compact: true));
        if (chat.Type != "direct")
            actions.Children.Add(IconButton("PeopleOutlined", "Участники", ToggleMembers, compact: true));
        actions.Children.Add(IconButton("Search", "Поиск сообщений", () => Run(SearchMessages), compact: true));
        if (PluginActions(ClientUiLocation.Chat) is { Count: > 0 } pluginActions)
            actions.Children.Add(ConversationMenu(pluginActions));
        Grid.SetColumn(actions, 2);
        toolbar.Children.Add(actions);
        content.Children.Add(Surface(toolbar, "panel"));
        conversationLesson = new Border { Margin = new Thickness(28, 14, 28, 0) };
        Grid.SetRow(conversationLesson, 1);
        content.Children.Add(conversationLesson);
        Grid.SetRow(messageScroll, 2);
        content.Children.Add(messageScroll);
        var composer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,40"),
            ColumnSpacing = 8,
        };
        var input = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("40,*"),
            ColumnSpacing = 2,
        };
        attachFileButton = IconButton(
            "Add",
            "Прикрепить файл",
            () => Run(UploadFile),
            compact: true
        );
        attachFileButton.VerticalAlignment = VerticalAlignment.Bottom;
        input.Children.Add(attachFileButton);
        messageInput.VerticalContentAlignment = VerticalAlignment.Center;
        Grid.SetColumn(messageInput, 1);
        input.Children.Add(messageInput);
        composer.Children.Add(Surface(input, "surface", new Thickness(2, 0), 8));
        sendMessageButton = IconButton(
            "SendOutlined",
            "Отправить сообщение",
            () => Run(SendMessage),
            compact: true
        );
        sendMessageButton.Classes.Add("primary");
        sendMessageButton.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(sendMessageButton, 1);
        composer.Children.Add(sendMessageButton);
        var footer = Column(attachmentList, composerNotice, composer);
        footer.Spacing = 8;
        var composerSurface = Surface(footer, "chrome", new Thickness(16));
        Grid.SetRow(composerSurface, 3);
        content.Children.Add(composerSurface);
        conversation.Children.Add(content);
        RefreshAttachments();
    }

    private Button ConversationMenu(IReadOnlyList<ClientUiAction> actions)
    {
        var button = IconButton(
            "MoreHoriz",
            "Действия с чатом",
            () => Task.CompletedTask,
            compact: true
        );
        var menu = new ContextMenu();
        void AddAction(string title, Func<Task> action)
        {
            var item = new MenuItem { Header = title };
            item.Click += async (_, _) => await Run(action);
            menu.Items.Add(item);
        }
        foreach (var action in actions)
            AddAction(action.Title, action.Execute);
        button.ContextMenu = menu;
        button.Click += (_, _) => menu.Open(button);
        return button;
    }

    private async Task LoadConversationMembers()
    {
        if (client is not { } source || selectedChat is not { } chat)
            return;
        var members = await source.Get<MemberInfo[]>($"api/chats/{chat.Id}/members");
        if (client != source || selectedChat?.Id != chat.Id)
            return;
        conversationMembers = members;
        if (chat.Type != "direct")
            BuildMemberPanel(source, chat, members);
        UpdateComposer();
    }

    private Task ToggleMembers()
    {
        membersOpen = !Views.WorkspaceView.ShowsMembers(Bounds.Width, membersOpen);
        UpdateLayoutMode();
        return Task.CompletedTask;
    }

    private void BuildMemberPanel(MessengerClient source, ChatInfo chat, MemberInfo[] members)
    {
        memberList.Children.Clear();
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = Text($"Участники · {members.Length}", 16, strong: true);
        title.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(title);
        var close = IconButton("Close", "Закрыть список участников", ToggleMembers, compact: true);
        Grid.SetColumn(close, 1);
        heading.Children.Add(close);
        memberList.Children.Add(heading);
        if (CanManageChat(chat) && !chat.Managed && chat.GuildId is null && chat.Type == "group")
            memberList.Children.Add(Button("Добавить участника", () => Run(() => AddChatMember(source, chat))));
        foreach (var member in members.OrderByDescending(member => member.Role == "owner").ThenBy(MemberName))
        {
            memberList.Children.Add(ChatMemberRow(source, chat, member));
        }
        if (chat.Managed)
            memberList.Children.Add(Text(ManagedDescription(), 12, "muted"));
    }

    private void RefreshLessonBanner()
    {
        if (conversationLesson is null || selectedChat is null)
            return;
        var chat = selectedChat;
        var call = availableCalls.FirstOrDefault(call =>
            call.Status == "live"
            && (
                call.ChatId == chat.Id
                || chat.Type != "room"
                    && chat.GuildId is not null
                    && chats.Any(channel =>
                        channel.Id == call.ChatId && channel.GuildId == chat.GuildId
                    )
            )
        );
        if (chat.Type == "room")
        {
            var join = Primary(
                "Войти в голосовую комнату",
                () => Run(call is null ? StartCall : () => OpenCall(call.Id))
            );
            join.HorizontalAlignment = HorizontalAlignment.Left;
            var description = Text(
                call is null
                    ? "В учебной комнате занятие сначала запускает преподаватель."
                    : "Звонок уже идёт.",
                13,
                "secondary"
            );
            description.IsVisible = call is not null || chat.Managed && !chat.CanManage;
            conversationLesson.Child = Card(
                Column(Text("Голосовая комната", 16, strong: true), description, join)
            );
            conversationLesson.IsVisible = true;
            return;
        }
        conversationLesson.IsVisible = call is not null;
        conversationLesson.Child = call is null ? null : LessonCard(call);
    }

    private void UpdateComposer()
    {
        var member = conversationMembers.FirstOrDefault(member => member.UserId == profile?.Id);
        var canWrite = member?.CanWrite != false;
        if (sendMessageButton is not null)
            sendMessageButton.IsEnabled =
                canWrite
                && !sending
                && (!string.IsNullOrWhiteSpace(messageInput.Text) || attachments.Count > 0);
        if (attachFileButton is not null)
            attachFileButton.IsEnabled = canWrite && !sending;
        messageInput.IsReadOnly = !canWrite;
        composerNotice.Text = canWrite ? "" : "В этом чате вам доступно только чтение.";
        composerNotice.IsVisible = !canWrite;
    }

    private void RefreshAttachments()
    {
        attachmentList.Children.Clear();
        attachmentList.IsVisible = attachments.Count > 0;
        foreach (var file in attachments.ToArray())
        {
            var current = file;
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("24,*,40"),
                ColumnSpacing = 8,
            };
            row.Children.Add(
                new MaterialIcon("AttachFile", 20) { VerticalAlignment = VerticalAlignment.Center }
            );
            var name = Ellipsis(file.Name, 13);
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var remove = IconButton(
                "Close",
                $"Убрать {file.Name}",
                () =>
                {
                    attachments.Remove(current);
                    RefreshAttachments();
                    return Task.CompletedTask;
                },
                compact: true
            );
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            attachmentList.Children.Add(row);
        }
        UpdateComposer();
    }

    private Control CreateMessageRow(ChatMessage message)
    {
        var own = message.SenderId == profile?.Id;
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("32,*"),
            ColumnSpacing = 10,
            Background = Brushes.Transparent,
        };
        var avatar = Avatar(message.SenderName, message.SenderId, AvatarKind.User, 32);
        avatar.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(avatar);
        var name = Ellipsis(message.SenderName, 12, "secondary", true);
        var time = Text(
            DateTime.SpecifyKind(message.SentAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm"),
            11,
            "muted"
        );
        var labels = new WrapPanel { ItemSpacing = 10 };
        labels.Children.Add(name);
        labels.Children.Add(time);
        if (message.EditedAtUtc is not null)
            labels.Children.Add(Text("изменено", 11, "muted"));
        if (own && message.ReadCount > 0)
            labels.Children.Add(new MaterialIcon("DoneAll", 14));

        var body = Column(labels);
        body.Spacing = 5;
        body.VerticalAlignment = VerticalAlignment.Top;
        if (!string.IsNullOrWhiteSpace(message.Text))
            body.Children.Add(new MarkdownView(message.Text));
        foreach (var file in message.Attachments)
            body.Children.Add(FileAttachment(file));
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        var menu = MessageMenu(message);
        if (menu.Items.Count > 0)
            AttachContextMenu(grid, menu);
        return grid;
    }

    private Control FileAttachment(FileInfoDto file)
    {
        var preview = file.Size <= 1024 * 1024
            && Path.GetExtension(file.Name).ToLowerInvariant() is ".cs" or ".js" or ".ts" or ".py" or ".php" or ".json" or ".txt";
        var badge = Surface(
            new MaterialIcon(preview ? "Code" : "InsertDriveFileOutlined", 20),
            "accent", radius: 17
        );
        badge.Width = badge.Height = 34;
        badge.VerticalAlignment = VerticalAlignment.Center;
        badge.Child!.HorizontalAlignment = HorizontalAlignment.Center;
        badge.Child.VerticalAlignment = VerticalAlignment.Center;
        var description = Column(Ellipsis(file.Name, 14, strong: true), Text(FileSize(file.Size), 12, "muted"));
        description.Spacing = 2;
        description.VerticalAlignment = VerticalAlignment.Center;
        var main = Button(file.Name, () => Run(() => preview ? PreviewCode(file) : SaveFile(file)));
        main.Classes.Add("quiet");
        main.Padding = new Thickness(8, 6);
        main.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ToolTip.SetTip(main, preview ? "Открыть предпросмотр" : "Скачать файл");

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*"), ColumnSpacing = 10 };
        content.Children.Add(badge);
        Grid.SetColumn(description, 1);
        content.Children.Add(description);
        main.Content = content;

        var card = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MaxWidth = 440 };
        card.HorizontalAlignment = HorizontalAlignment.Left;
        card.Children.Add(main);
        if (preview)
        {
            var download = IconButton("Download", $"Скачать {file.Name}", () => Run(() => SaveFile(file)), compact: true);
            download.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(download, 1);
            card.Children.Add(download);
        }
        var surface = Surface(card, "panel", radius: 10);
        surface.HorizontalAlignment = HorizontalAlignment.Left;
        return surface;
    }

    private static string FileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} Б",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} КБ",
        _ => $"{bytes / (1024d * 1024):0.#} МБ",
    };

    private ContextMenu MessageMenu(ChatMessage message)
    {
        var menu = new ContextMenu();
        if (message.SenderId == profile?.Id)
        {
            var edit = new MenuItem { Header = "Изменить" };
            edit.Click += async (_, _) => await Run(() => EditMessage(message));
            menu.Items.Add(edit);
        }
        if (message.SenderId == profile?.Id || selectedChat?.CanManage == true)
        {
            var delete = new MenuItem { Header = "Удалить" };
            delete.Classes.Add("danger");
            delete.Click += async (_, _) =>
                await Run(async () =>
                {
                    await client!.Mutate($"api/messages/{message.Id}", HttpMethod.Delete);
                    await LoadMessages();
                });
            menu.Items.Add(delete);
        }
        return menu;
    }
}
