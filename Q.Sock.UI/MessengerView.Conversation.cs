using System.Net.Http;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Q.Sock.Core;
using Q.Sock.Shared.Models;
using Q.Sock.UI.Plugins;
using static Q.Sock.UI.UiFactory;

namespace Q.Sock.UI;

public sealed partial class MessengerView
{
    private TextBlock? conversationHeaderTitle;
    private TextBlock? conversationSubtitle;
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
        conversationSubtitle = Text(
            chat.Managed ? "Управляется интеграцией"
                : chat.Type == "direct" ? "Личный чат"
                : "Участники чата",
            11,
            "muted"
        );
        var heading = Column(conversationHeaderTitle, conversationSubtitle);
        heading.Spacing = 2;
        heading.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(heading, 1);
        toolbar.Children.Add(heading);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (chat.Type != "room")
            actions.Children.Add(IconButton("Call", "Звонок", () => Run(StartCall), compact: true));
        actions.Children.Add(
            IconButton("PeopleOutlined", "Участники", () => Run(ShowMembers), compact: true)
        );
        actions.Children.Add(ConversationMenu(chat));
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

    private Button ConversationMenu(ChatInfo chat)
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
        AddAction("Поиск сообщений", SearchMessages);
        AddAction("Участники", ShowMembers);
        foreach (var action in PluginActions(ClientUiLocation.Chat))
            AddAction(action.Title, action.Execute);
        button.ContextMenu = menu;
        button.Click += (_, _) => menu.Open(button);
        return button;
    }

    private async Task LoadConversationMembers()
    {
        if (client is null || selectedChat is null)
            return;
        var id = selectedChat.Id;
        var members = await client.Get<MemberInfo[]>($"api/chats/{id}/members");
        if (selectedChat?.Id != id)
            return;
        conversationMembers = members;
        if (conversationSubtitle is not null)
            conversationSubtitle.Text =
                selectedChat.Type == "direct" ? "Личный чат" : $"{members.Length} участников";
        memberList.Children.Clear();
        memberList.Children.Add(Text($"Участники · {members.Length}", 18, strong: true));
        foreach (
            var member in members
                .OrderByDescending(member => member.Role == "owner")
                .ThenBy(member => member.DisplayName)
        )
        {
            var name = string.IsNullOrWhiteSpace(member.DisplayName)
                ? member.UserName
                : member.DisplayName;
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("32,*"),
                ColumnSpacing = 10,
            };
            row.Children.Add(Avatar(name, member.UserId, AvatarKind.User, 32));
            var copy = Column(Ellipsis(name, 13, "secondary"));
            copy.Spacing = 1;
            if (member.Role != "member")
                copy.Children.Add(Text(ExtensionRoleCaption(member.Role), 11, "muted"));
            Grid.SetColumn(copy, 1);
            row.Children.Add(copy);
            memberList.Children.Add(row);
        }
        if (selectedChat.Managed)
            memberList.Children.Add(Text(ManagedDescription(), 12, "muted"));
        if (selectedChat.CanManage && selectedChat.Type != "direct")
            memberList.Children.Add(Button("Управление участниками", () => Run(ShowMembers)));
        UpdateComposer();
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
        };
        grid.Children.Add(Avatar(message.SenderName, message.SenderId, AvatarKind.User, 32));
        var heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,32"),
            ColumnSpacing = 4,
        };
        var name = Ellipsis(message.SenderName, 12, "secondary", true);
        var time = Text(
            DateTime
                .SpecifyKind(message.SentAtUtc, DateTimeKind.Utc)
                .ToLocalTime()
                .ToString("HH:mm"),
            11,
            "muted"
        );
        var labels = new WrapPanel
        {
            ItemSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        labels.Children.Add(name);
        labels.Children.Add(time);
        if (message.EditedAtUtc is not null)
            labels.Children.Add(Text("изменено", 11, "muted"));
        if (own && message.ReadCount > 0)
            labels.Children.Add(new MaterialIcon("DoneAll", 14));
        heading.Children.Add(labels);
        var menu = MessageMenu(message);
        if (menu.Items.Count > 0)
        {
            var options = IconButton(
                "MoreHoriz",
                "Действия с сообщением",
                () => Task.CompletedTask,
                compact: true
            );
            options.Width = 32;
            options.Height = options.MinHeight = 28;
            options.ContextMenu = menu;
            options.Click += (_, _) => menu.Open(options);
            Grid.SetColumn(options, 1);
            heading.Children.Add(options);
        }
        var body = Column(heading, new MarkdownView(message.Text));
        body.Spacing = 3;
        foreach (var file in message.Attachments)
        {
            var current = file;
            var fileContent = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("32,*"),
                ColumnSpacing = 12,
            };
            fileContent.Children.Add(
                new MaterialIcon("InsertDriveFileOutlined", 28)
                {
                    VerticalAlignment = VerticalAlignment.Center,
                }
            );
            var description = Column(
                Text(file.Name, 14, strong: true),
                Text($"{Math.Ceiling(file.Size / 1024d)} КБ · скачать файл", 12, "muted")
            );
            description.Spacing = 4;
            Grid.SetColumn(description, 1);
            fileContent.Children.Add(description);
            var download = Button(file.Name, () => Run(() => SaveFile(current)));
            download.Content = fileContent;
            download.Classes.Add("quiet");
            download.HorizontalAlignment = HorizontalAlignment.Stretch;
            download.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            body.Children.Add(Surface(download, "panel", new Thickness(4), 10));
            if (
                Path.GetExtension(file.Name)
                is ".cs"
                    or ".js"
                    or ".ts"
                    or ".py"
                    or ".php"
                    or ".json"
                    or ".txt"
            )
                body.Children.Add(
                    IconButton("Code", "Просмотреть код", () => Run(() => PreviewCode(current)))
                );
        }
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        grid.ContextMenu = menu;
        return grid;
    }

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
