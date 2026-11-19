using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
    private Border? callChatPanel;
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
        callContent.Children.Add(Surface(session.View, "popup", new Thickness(8), 12));
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
        controls.Children.Add(
            IconButton(
                "PeopleOutlined",
                "Участники",
                () => Run(() => ShowCallMembers(call)),
                compact: true
            )
        );
        controls.Children.Add(
            IconButton("ChatBubbleOutlineOutlined", "Обсуждение", OpenCallDiscussion, compact: true)
        );
        var leave = IconButton("CallEnd", "Выйти из звонка", () => Run(LeaveCall), compact: true);
        leave.Classes.Add("danger");
        controls.Children.Add(leave);
        if (callChat?.CanManage == true || callChat?.Type == "direct")
            controls.Children.Add(
                Button(
                    "Завершить для всех",
                    () =>
                        Run(async () =>
                        {
                            await client.Mutate($"api/calls/{callId}/end", HttpMethod.Post);
                            await LeaveCall();
                        })
                )
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

    private void UpdateCallLayout()
    {
        if (callContent is null || callChatPanel is null)
            return;
        var wide = Bounds.Width >= 1100;
        callContent.ColumnDefinitions[1].Width = new GridLength(wide ? 320 : 0);
        callContent.ColumnSpacing = wide ? 20 : 0;
        callChatPanel.IsVisible = wide;
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
        callChatPanel = null;
        callDiscussion = null;
        callDiscussionChat = null;
        if (overlay is not null)
            root.Children.Remove(overlay);
        if (session is not null)
            await session.DisposeAsync();
    }

    private async Task ShowCallMembers(CallInfo call)
    {
        var members = await client!.Get<System.Text.Json.JsonElement[]>(
            $"api/calls/{call.Id}/members"
        );
        var chat = chats.FirstOrDefault(chat => chat.Id == call.ChatId);
        var canModerate = chat?.CanManage == true && chat.Type != "direct";
        var list = new StackPanel { Spacing = 12 };
        foreach (var member in members)
        {
            var userId = member.GetProperty("userId").GetGuid();
            var item = Column(Text(member.GetProperty("name").GetString() ?? "Участник"));
            if (canModerate && userId != profile?.Id)
            {
                var muted = member.GetProperty("muted").GetBoolean();
                var share = member.GetProperty("canShare").GetBoolean();
                async Task Moderate(string action)
                {
                    await client.Mutate(
                        $"api/calls/{call.Id}/members/{userId}/{action}",
                        System.Net.Http.HttpMethod.Post
                    );
                    await ShowCallMembers(call);
                }
                item.Children.Add(
                    Button(
                        muted ? "Разрешить микрофон" : "Отключить микрофон",
                        () => Run(() => Moderate(muted ? "allow-mic" : "mute"))
                    )
                );
                item.Children.Add(
                    Button(
                        share ? "Запретить демонстрацию" : "Разрешить демонстрацию",
                        () => Run(() => Moderate(share ? "deny-share" : "allow-share"))
                    )
                );
                item.Children.Add(
                    Button("Исключить из звонка", () => Run(() => Moderate("remove")))
                );
            }
            list.Children.Add(Card(item));
        }
        await ShowDialog("Участники звонка", list);
    }
}
