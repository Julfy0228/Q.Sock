using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Q.Sock.Shared.Models;
using Q.Sock.UI.Calls;
using static Q.Sock.UI.UiFactory;

namespace Q.Sock.UI;

public sealed partial class MessengerView
{
    private ICallSession? activeCall;
    private Guid? activeCallId;
    private Border? callOverlay;
    private Grid? callContent;
    private Border? callVideoPanel;
    private Border? callChatPanel;
    private Border? callMembersPanel;
    private bool callMembersOpen;
    private StackPanel? callDiscussion;
    private Guid? callDiscussionChat;

    private async Task ShowCall(Guid callId)
    {
        if (activeCallId == callId && callOverlay is not null)
        {
            callOverlay.IsVisible = true;
            activeCall!.View.IsVisible = true;
            return;
        }
        await LeaveCall();
        var call = await client!.Get<CallInfo>($"api/calls/{callId}");
        var notice = Text("", 12, "muted");
        var session = callPlatform!.Create(
            client,
            message => Dispatcher.UIThread.Post(() => notice.Text = message)
        );
        session.ApplyTheme(currentTheme);
        activeCall = session;
        activeCallId = callId;
        var state = Text("Подключение…", 12, "muted");
        var microphone = IconButton(
            "MicOff",
            "Включить микрофон",
            () => Run(session.ToggleMicrophone),
            compact: true
        );
        var camera = IconButton(
            "VideocamOffOutlined",
            "Включить камеру",
            () => Run(session.ToggleCamera),
            compact: true
        );
        var screen = IconButton(
            "ScreenShareOutlined",
            "Показать экран",
            () => Run(session.ToggleScreen),
            compact: true
        );
        session.Changed += media =>
            Dispatcher.UIThread.Post(() =>
            {
                state.Text = media.Connected
                    ? $"В эфире · {media.Participants} участников"
                    : "Звонок завершён";
                microphone.Content = new MaterialIcon(media.Microphone ? "Mic" : "MicOff", 22);
                camera.Content = new MaterialIcon(
                    media.Camera ? "VideocamOutlined" : "VideocamOffOutlined",
                    22
                );
                screen.Content = new MaterialIcon(
                    media.Sharing ? "StopScreenShareOutlined" : "ScreenShareOutlined",
                    22
                );
                ToolTip.SetTip(
                    microphone,
                    media.Microphone ? "Выключить микрофон" : "Включить микрофон"
                );
                ToolTip.SetTip(camera, media.Camera ? "Выключить камеру" : "Включить камеру");
                ToolTip.SetTip(screen, media.Sharing ? "Остановить показ" : "Показать экран");
            });
        var callChat = chats.FirstOrDefault(chat => chat.Id == call.ChatId);
        var displayTitle = callChat?.Type == "direct" ? callChat.Name : call.Title;
        var title = Column(Text(displayTitle, 18, strong: true), state);
        title.Spacing = 2;
        var heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(20, 16),
        };
        heading.Children.Add(title);
        var back = IconButton("ExpandMore", "Свернуть звонок", OpenCallDiscussion, compact: true);
        Grid.SetColumn(back, 1);
        heading.Children.Add(back);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        layout.Children.Add(Surface(heading, "panel"));
        callContent = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,0"),
            Margin = new Thickness(20, 16),
        };
        callVideoPanel = Surface(session.View, "popup", new Thickness(8), 12);
        callContent.Children.Add(callVideoPanel);
        callMembersPanel = Surface(new ScrollViewer(), "panel", radius: 12);
        callContent.Children.Add(callMembersPanel);
        callDiscussionChat = callChat?.GuildId is { } guild
            ? chats.FirstOrDefault(chat => chat.GuildId == guild && chat.Type == "channel")?.Id
                ?? call.ChatId
            : call.ChatId;
        callDiscussion = new StackPanel { Spacing = 18 };
        var discussionInput = Input("Сообщение в чат звонка…");
        discussionInput.Classes.Add("composer");
        var discussionSend = Primary(
            "Отправить",
            () =>
                Run(async () =>
                {
                    var value = discussionInput.Text?.Trim();
                    if (string.IsNullOrEmpty(value) || callDiscussionChat is not { } chatId)
                        return;
                    await client.Post<ChatMessage>(
                        $"api/chats/{chatId}/messages",
                        new SendMessageRequest { Text = value, ClientOperationId = Guid.NewGuid() }
                    );
                    discussionInput.Text = "";
                    await RefreshCallDiscussion();
                })
        );
        discussionSend.IsEnabled = false;
        discussionInput.TextChanged += (_, _) =>
            discussionSend.IsEnabled = !string.IsNullOrWhiteSpace(discussionInput.Text);
        var discussionPanel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 18,
            Margin = new Thickness(20),
        };
        discussionPanel.Children.Add(Text("Чат звонка", 18, strong: true));
        var discussionScroll = new ScrollViewer { Content = callDiscussion };
        Grid.SetRow(discussionScroll, 1);
        discussionPanel.Children.Add(discussionScroll);
        var editor = Column(discussionInput, discussionSend);
        Grid.SetRow(editor, 2);
        discussionPanel.Children.Add(editor);
        callChatPanel = Surface(discussionPanel, "panel", radius: 12);
        Grid.SetColumn(callChatPanel, 1);
        callContent.Children.Add(callChatPanel);
        Grid.SetRow(callContent, 1);
        layout.Children.Add(callContent);
        var controls = new WrapPanel
        {
            ItemSpacing = 12,
            LineSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        controls.Children.Add(microphone);
        controls.Children.Add(camera);
        controls.Children.Add(screen);
        if (callChat?.Type != "direct")
            controls.Children.Add(
                IconButton(
                    "PeopleOutlined", "Участники",
                    () => Run(() => ToggleCallMembers(call)), compact: true
                )
            );
        controls.Children.Add(
            IconButton("ChatBubbleOutlineOutlined", "Обсуждение", OpenCallDiscussion, compact: true)
        );
        controls.Children.Add(
            CallEndControl(callId, client, callChat is not null && (CanManageChat(callChat) || callChat.Type == "direct"))
        );
        var footer = Column(controls, notice);
        footer.Spacing = 4;
        var footerSurface = Surface(footer, "chrome", new Thickness(16));
        Grid.SetRow(footerSurface, 2);
        layout.Children.Add(footerSurface);
        callOverlay = Surface(layout, "background");
        callOverlay.ZIndex = 10;
        CloseDialog();
        root.Children.Add(callOverlay);
        UpdateCallLayout();
        try
        {
            await session.Join(call);
            await RefreshCallDiscussion();
        }
        catch
        {
            await LeaveCall();
            throw;
        }
    }

    private Control CallEndControl(Guid callId, Q.Sock.Core.MessengerClient source, bool canEnd)
    {
        var leave = IconButton("CallEnd", "Выйти из звонка", () => Run(LeaveCall), compact: true);
        leave.Classes.Add("danger");
        if (!canEnd)
            return leave;

        leave.CornerRadius = new CornerRadius(20, 0, 0, 20);
        var menu = new ContextMenu();
        AddMemberAction(menu, "Завершить для всех", async () =>
        {
            await source.Mutate($"api/calls/{callId}/end", HttpMethod.Post);
            if (activeCallId == callId)
                await LeaveCall();
        }, destructive: true);
        var more = IconButton("ExpandMore", "Варианты завершения звонка", () => Task.CompletedTask, compact: true);
        more.Width = 28;
        more.CornerRadius = new CornerRadius(0, 20, 20, 0);
        more.Classes.Add("danger");
        more.Click += (_, _) => menu.Open(more);
        AttachContextMenu(more, menu);
        var split = Row(leave, more);
        split.Spacing = 0;
        return split;
    }

    private void UpdateCallLayout()
    {
        if (callContent is null || callChatPanel is null || callMembersPanel is null || callVideoPanel is null)
            return;
        var wide = Bounds.Width >= 1100;
        callContent.ColumnDefinitions[1].Width = new GridLength(wide ? 320 : 0);
        callContent.ColumnSpacing = wide ? 20 : 0;
        callChatPanel.IsVisible = wide && !callMembersOpen;
        callVideoPanel.IsVisible = wide || !callMembersOpen;
        callMembersPanel.IsVisible = callMembersOpen;
        Grid.SetColumn(callMembersPanel, wide ? 1 : 0);
        callContent.Margin = new Thickness(Bounds.Width < 760 ? 12 : 20, 16);
    }

    private async Task OpenCallDiscussion()
    {
        if (callOverlay is null)
            return;
        if (chats.FirstOrDefault(chat => chat.Id == callDiscussionChat) is { } chat)
            await SelectChat(chat);
        callOverlay.IsVisible = false;
        if (activeCall is not null)
            activeCall.View.IsVisible = false;
    }

    private async Task RefreshCallDiscussion()
    {
        if (client is null || callDiscussionChat is not { } chatId || callDiscussion is null)
            return;
        var panel = callDiscussion;
        try
        {
            var messages = await client.Get<ChatMessage[]>($"api/chats/{chatId}/messages");
            if (callDiscussion != panel)
                return;
            panel.Children.Clear();
            foreach (var message in messages.OrderBy(message => message.SentAtUtc).TakeLast(20))
                panel.Children.Add(
                    Column(
                        Text(message.SenderName, 12, "secondary", true),
                        new MarkdownView(message.Text)
                    )
                );
            if (messages.Length == 0)
                panel.Children.Add(
                    Text("Сообщения участникам звонка появятся здесь.", 13, "muted")
                );
        }
        catch (HttpRequestException)
        {
            if (callDiscussion == panel)
            {
                panel.Children.Clear();
                panel.Children.Add(
                    Text(
                        "Не удалось загрузить обсуждение. Сам звонок продолжает работать.",
                        12,
                        "muted"
                    )
                );
            }
        }
    }

    private async Task LeaveCall()
    {
        var session = activeCall;
        var overlay = callOverlay;
        activeCall = null;
        activeCallId = null;
        callOverlay = null;
        callContent = null;
        callVideoPanel = null;
        callChatPanel = null;
        callMembersPanel = null;
        callMembersOpen = false;
        callDiscussion = null;
        callDiscussionChat = null;
        if (overlay is not null)
            root.Children.Remove(overlay);
        if (session is not null)
            await session.DisposeAsync();
    }

    private async Task ToggleCallMembers(CallInfo call)
    {
        callMembersOpen = !callMembersOpen;
        UpdateCallLayout();
        if (callMembersOpen)
            await LoadCallMembers(call);
    }

    private async Task LoadCallMembers(CallInfo call)
    {
        if (client is not { } source || callMembersPanel?.Child is not ScrollViewer scroll)
            return;
        var members = await source.Get<System.Text.Json.JsonElement[]>($"api/calls/{call.Id}/members");
        if (client != source || activeCallId != call.Id || callMembersPanel?.Child != scroll)
            return;

        var chat = chats.FirstOrDefault(chat => chat.Id == call.ChatId);
        var canModerate = chat is { Type: not "direct" } && CanManageChat(chat);
        var list = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var heading = Text($"Участники звонка · {members.Length}", 16, strong: true);
        heading.VerticalAlignment = VerticalAlignment.Center;
        title.Children.Add(heading);
        var close = IconButton(
            "Close", "Закрыть список участников звонка",
            () => Run(() => ToggleCallMembers(call)), compact: true
        );
        Grid.SetColumn(close, 1);
        title.Children.Add(close);
        list.Children.Add(title);
        foreach (var member in members)
        {
            var userId = member.GetProperty("userId").GetGuid();
            var name = member.GetProperty("name").GetString() ?? "Участник";
            var item = PersonIdentity(name, userId);
            if (!canModerate || userId == profile?.Id)
            {
                list.Children.Add(Surface(item, "panel", new Thickness(6, 5)));
                continue;
            }

            var muted = member.GetProperty("muted").GetBoolean();
            var share = member.GetProperty("canShare").GetBoolean();
            async Task Moderate(string action)
            {
                await source.Mutate($"api/calls/{call.Id}/members/{userId}/{action}", HttpMethod.Post);
                await LoadCallMembers(call);
            }
            var menu = new ContextMenu();
            AddMemberAction(menu, muted ? "Разрешить говорить" : "Запретить говорить",
                () => Moderate(muted ? "allow-mic" : "mute"));
            AddMemberAction(menu, share ? "Запретить показ экрана" : "Разрешить показ экрана",
                () => Moderate(share ? "deny-share" : "allow-share"));
            AddMemberAction(menu, "Исключить из звонка", () => Moderate("remove"), destructive: true);
            var button = Button($"Действия: {name}", () => Task.CompletedTask);
            button.Classes.Add("quiet");
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Content = item;
            button.Click += (_, _) => menu.Open(button);
            AttachContextMenu(button, menu);
            list.Children.Add(button);
        }
        scroll.Content = list;
    }
}
