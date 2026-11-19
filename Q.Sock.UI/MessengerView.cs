using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Q.Sock.Core;
using Q.Sock.Core.Plugins;
using Q.Sock.Shared.Models;
using Q.Sock.Shared.Models.Auth;
using Q.Sock.UI.Calls;
using Q.Sock.UI.Views;
using Q.Sock.UI.Plugins;
using static Q.Sock.UI.UiFactory;

namespace Q.Sock.UI;

public sealed partial class MessengerView : UserControl
{
    private readonly IAccountRepository accountRepository;
    private readonly string? browserOrigin;
    private readonly string? launchCode;
    private readonly ICallPlatform? callPlatform;
    private readonly Dictionary<string, IClientPlugin> clientPlugins;
    private readonly Dictionary<string, IClientPlugin> builtInPlugins;
    private readonly IPluginCache pluginCache;
    private readonly PublisherTrustStore pluginTrust;
    private readonly IPluginSignatureVerifier pluginSignatures;
    private readonly DispatcherTimer pluginTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly IClientPreferences preferences;
    private readonly IQrScanner? qrScanner;
    private readonly IDisplaySettings? displaySettings;
    private readonly List<SavedAccount> accounts = [];
    private readonly Grid root = new();
    private readonly WorkspaceView workspaceView = new();
    private Grid workspace => workspaceView.Workspace;
    private StackPanel navigation => workspaceView.Navigation;
    private Grid conversation => workspaceView.Conversation;
    private TextBlock status => workspaceView.Status;
    private readonly ScrollViewer messageScroll = new();
    private readonly StackPanel messageList = new()
    {
        Spacing = 22,
        Margin = new Thickness(28, 20),
    };
    private readonly TextBox messageInput = Input("Написать сообщение…");
    private readonly List<FileInfoDto> attachments = [];
    private readonly string cacheDirectory;
    private MessengerClient? client;
    private SavedAccount? account;
    private Profile? profile;
    private ChatInfo[] chats = [];
    private GuildInfo[] guilds = [];
    private ChatInfo? selectedChat;
    private Guid? selectedGuild;
    private bool showConversation;
    private bool refreshing;
    private bool sending;
    private Guid? operationId;
    private UiManifest? manifest;
    private ServerInfo? serverInfo;
    private bool refreshAgain;
    private bool refreshConversationAgain;
    private string messageSearch = "";
    private int historyPages = 1;
    private int historyRequest;

    public MessengerView(
        IAccountRepository accountRepository,
        string? browserOrigin = null,
        string? launchCode = null,
        ICallPlatform? callPlatform = null,
        IEnumerable<IClientPlugin>? plugins = null,
        IClientPreferences? preferences = null,
        IQrScanner? qrScanner = null,
        IDisplaySettings? displaySettings = null,
        IPluginCache? pluginCache = null,
        IPluginSignatureVerifier? pluginSignatures = null
    )
    {
        this.accountRepository = accountRepository;
        this.browserOrigin = browserOrigin;
        this.launchCode = launchCode;
        this.qrScanner = qrScanner;
        this.displaySettings = displaySettings;
        this.callPlatform = callPlatform;
        builtInPlugins = (plugins ?? []).ToDictionary(plugin => plugin.Id);
        clientPlugins = new(builtInPlugins);
        cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Q.Sock",
            "ui-cache"
        );
        this.preferences =
            preferences ?? new FileClientPreferences(Path.GetDirectoryName(cacheDirectory)!);
        this.pluginCache =
            pluginCache ?? new FilePluginCache(Path.Combine(cacheDirectory, "packages"));
        pluginTrust = new PublisherTrustStore(this.preferences);
        this.pluginSignatures = pluginSignatures ?? new ManagedPluginSignatureVerifier();
        pluginTimer.Tick += async (_, _) =>
        {
            if (
                client is not null
                && !showingLogin
                && dialog is null
                && activeCall is null
                && customWorkspace is null
            )
                await Run(() => RefreshClientPlugins());
        };
        TopLevel.SetAutoSafeAreaPadding(this, true);
        ConfigureThemeStyles();
        Background = Brush.Parse("#313338");
        Content = root;
        messageScroll.Content = messageList;
        messageScroll.HorizontalScrollBarVisibility = Avalonia
            .Controls
            .Primitives
            .ScrollBarVisibility
            .Disabled;
        messageInput.AcceptsReturn = true;
        messageInput.MinHeight = 40;
        messageInput.Classes.Add("composer");
        messageInput.TextChanged += (_, _) => UpdateComposer();
        messageInput.MaxHeight = 140;
        messageInput.AddHandler(
            KeyDownEvent,
            async (_, args) =>
            {
                if (args.Key == Key.Enter && !args.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    args.Handled = true;
                    await Run(SendMessage);
                }
            },
            RoutingStrategies.Tunnel
        );
        SizeChanged += (_, _) => UpdateLayoutMode();
        AttachedToVisualTree += async (_, _) => await Run(Initialize);
        DetachedFromVisualTree += async (_, _) =>
        {
            pluginTimer.Stop();
            await LeaveCall();
            await UnloadClientPlugins();
            if (client is not null)
                await client.DisposeAsync();
        };
    }

    private async Task Initialize()
    {
        if (await preferences.Read("theme") is { } theme)
        {
            try
            {
                currentTheme = MessengerTheme.Parse(theme);
                if (MessengerStyles.IsPreviousDefault(currentTheme))
                {
                    currentTheme = new MessengerTheme();
                    await preferences.Write("theme", currentTheme.Export());
                }
            }
            catch (ArgumentException)
            {
                currentTheme = new();
            }
        }
        ApplyTheme();
        accounts.Clear();
        accounts.AddRange(await accountRepository.LoadAsync());
        if (!string.IsNullOrWhiteSpace(launchCode) && browserOrigin is not null)
        {
            await AuthenticateLaunch();
            return;
        }
        if (browserOrigin is not null)
        {
            await using var transport = new MessengerClient(browserOrigin);
            serverInfo = await transport.PublicGet<ServerInfo>("api/server");
        }
        ShowLogin();
        if (browserOrigin is null && refreshLoginIdentity is { } refreshIdentity)
            await refreshIdentity();
        if (accounts.LastOrDefault() is { } saved)
        {
            try
            {
                await ConnectAccount(saved);
            }
            catch (HttpRequestException)
            {
                status.Text = "Сохранённая сессия недоступна. Войдите повторно.";
                ShowLogin();
            }
        }
    }

    private async Task AuthenticateLaunch()
    {
        await using var transport = new MessengerClient(browserOrigin!);
        var response = await transport.PublicPost<LaunchResponse>(
            "api/auth/launch",
            new
            {
                code = launchCode,
                deviceId = await accountRepository.GetOrCreateDeviceIdAsync(),
                deviceName = "Браузер · Avalonia",
            }
        );
        transport.Restore(response.Auth);
        var identity = await transport.Get<Profile>("api/me");
        var saved = new SavedAccount
        {
            ServerUrl = transport.Server.AbsoluteUri,
            UserName = identity.UserName,
        };
        UpdateSession(saved, response.Auth);
        accounts.RemoveAll(item =>
            item.ServerUrl == saved.ServerUrl && item.UserName == saved.UserName
        );
        accounts.Add(saved);
        await accountRepository.SaveAsync(accounts);
        await ConnectAccount(saved);
        if (
            response.ChatId is { } chatId
            && chats.FirstOrDefault(item => item.Id == chatId) is { } chat
        )
            await SelectChat(chat);
        if (response.CallId is { } callId)
            await OpenCall(callId);
    }

    private async Task Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            status.Text = exception.Message;
            await ShowDialog("Не удалось выполнить действие", Column(Text(exception.Message)));
        }
    }

    private async Task Authenticate(string server, string username, string password, bool register)
    {
        await using var transport = new MessengerClient(server);
        var device = await accountRepository.GetOrCreateDeviceIdAsync();
        var auth = await transport.Login(
            username,
            password,
            device,
            Environment.MachineName,
            register
        );
        var saved = accounts.FirstOrDefault(item =>
            item.ServerUrl == transport.Server.AbsoluteUri && item.UserName == username
        );
        if (saved is null)
        {
            saved = new() { ServerUrl = transport.Server.AbsoluteUri, UserName = username };
            accounts.Add(saved);
        }
        UpdateSession(saved, auth);
        await accountRepository.SaveAsync(accounts);
        await ConnectAccount(saved);
    }

    private static void UpdateSession(SavedAccount saved, AuthResponse auth)
    {
        saved.AccessToken = auth.AccessToken;
        saved.RefreshToken = auth.RefreshToken;
        saved.SessionId = auth.SessionId;
        saved.ExpiresAtUtc = auth.ExpiresAtUtc;
    }

    private async Task ConnectAccount(SavedAccount saved)
    {
        await LeaveCall();
        await UnloadClientPlugins();
        if (client is not null)
            await client.DisposeAsync();
        client = new(saved.ServerUrl);
        var connectedClient = client;
        account = saved;
        selectedChat = null;
        selectedGuild = null;
        showConversation = false;
        client.Restore(
            new()
            {
                AccessToken = saved.AccessToken,
                RefreshToken = saved.RefreshToken,
                SessionId = saved.SessionId,
                ExpiresAtUtc = saved.ExpiresAtUtc,
            }
        );
        client.SessionChanged += session =>
        {
            UpdateSession(saved, session);
            Dispatcher.UIThread.Post(async () => await accountRepository.SaveAsync(accounts));
        };
        client.Changed += change =>
            Dispatcher.UIThread.Post(
                async () => await Run(() => HandleWorkspaceChange(connectedClient, change))
            );
        client.SessionRevoked += () =>
            Dispatcher.UIThread.Post(async () =>
            {
                if (client != connectedClient)
                    return;
                await LeaveCall();
                accounts.Remove(saved);
                await accountRepository.SaveAsync(accounts);
                client = null;
                await UnloadClientPlugins();
                await connectedClient.DisposeAsync();
                status.Text = "Сессия завершена";
                ShowLogin();
            });
        client.IncomingCall += incoming =>
            Dispatcher.UIThread.Post(async () =>
            {
                var call = incoming.Deserialize<CallInfo>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                );
                if (call is not null)
                    await ShowDialog(
                        "Входящий звонок",
                        Column(
                            Text(call.Title),
                            Button("Ответить", () => Run(() => OpenCall(call.Id)))
                        )
                    );
            });
        await client.EnsureToken();
        profile = await client.Get<Profile>("api/me");
        await client.Connect();
        serverInfo = await client.PublicGet<ServerInfo>("api/server");
        manifest = await client.PublicGet<UiManifest>("api/ui/manifest");
        var descriptors = await new UiModuleCache(cacheDirectory).Update(client);
        foreach (var descriptor in descriptors.Values)
            descriptor.Dispose();
        await RefreshClientPlugins(refreshNavigation: false);
        BuildWorkspace();
        await RefreshWorkspace();
        ApplyPluginShell();
        pluginTimer.Start();
    }

    private async Task HandleWorkspaceChange(MessengerClient source, JsonElement change)
    {
        if (client != source)
            return;
        if (
            change.TryGetProperty("kind", out var kind)
            && kind.GetString() is "message" or "read"
            && change.TryGetProperty("chatId", out var chat)
            && chat.TryGetGuid(out var chatId)
        )
        {
            try
            {
                if (selectedChat?.Id == chatId && showConversation)
                    await LoadMessages();
                if (client == source && callDiscussionChat == chatId)
                    await RefreshCallDiscussion();
            }
            finally
            {
                if (client == source)
                    await RefreshWorkspace(refreshConversation: false);
            }
            return;
        }
        await RefreshWorkspace();
    }

    private Task RefreshWorkspace() => RefreshWorkspace(refreshConversation: true);

    private async Task RefreshWorkspace(bool refreshConversation)
    {
        var source = client;
        if (source is null)
            return;
        if (refreshing)
        {
            refreshAgain = true;
            refreshConversationAgain |= refreshConversation;
            return;
        }
        refreshAgain = false;
        refreshConversationAgain = false;
        refreshing = true;
        try
        {
            var updatedChats = await source.Get<ChatInfo[]>("api/chats");
            if (client != source)
                return;
            chats = updatedChats;
            if (refreshConversation)
            {
                var updatedGuilds = await source.Get<GuildInfo[]>("api/guilds");
                var updatedCalls = await source.Get<CallInfo[]>("api/calls");
                if (client != source)
                    return;
                guilds = updatedGuilds;
                availableCalls = updatedCalls;
            }
            RebuildNavigation();
            if (refreshConversation && activeCall is not null)
                await RefreshCallDiscussion();
            if (client != source)
                return;
            if (selectedChat is not null)
            {
                selectedChat = chats.FirstOrDefault(item => item.Id == selectedChat.Id);
                if (selectedChat is not null)
                {
                    if (refreshConversation)
                    {
                        RefreshLessonBanner();
                        await LoadConversationMembers();
                        if (client == source && showConversation)
                            await LoadMessages();
                    }
                }
                else
                {
                    attachments.Clear();
                    showConversation = false;
                    ShowEmptyConversation(
                        "Чат больше недоступен",
                        "Выберите другой диалог или пространство."
                    );
                    UpdateLayoutMode();
                }
            }
        }
        finally
        {
            refreshing = false;
        }
        if (refreshAgain)
            await RefreshWorkspace(refreshConversationAgain);
    }

    private async Task SelectChat(ChatInfo chat)
    {
        selectedChat = chat;
        membersOpen = null;
        conversationMembers = [];
        memberList.Children.Clear();
        selectedGuild = chat.GuildId;
        navigationSection = chat.GuildId is null ? "Чаты" : "Пространства";
        messageSearch = "";
        historyPages = 1;
        attachments.Clear();
        messageInput.Text = "";
        operationId = null;
        BuildConversation(chat);
        showConversation = true;
        RebuildNavigation();
        UpdateLayoutMode();
        RefreshLessonBanner();
        await Task.WhenAll(LoadConversationMembers(), LoadMessages());
    }

    private async Task LoadMessages(bool showOlder = false)
    {
        var source = client;
        if (selectedChat is null || source is null)
            return;
        var currentChat = selectedChat.Id;
        var request = ++historyRequest;
        var query = Uri.EscapeDataString(messageSearch);
        var messages = new List<ChatMessage>();
        var hasOlder = false;
        for (var page = 0; page < historyPages; page++)
        {
            var batch = await source.Get<ChatMessage[]>(
                $"api/chats/{currentChat}/messages?offset={page * 100}&search={query}"
            );
            messages.AddRange(batch);
            hasOlder = batch.Length == 100;
            if (!hasOlder)
                break;
        }
        if (client != source || selectedChat?.Id != currentChat || request != historyRequest)
            return;
        messageList.Children.Clear();
        if (messageSearch.Length > 0)
            messageList.Children.Add(
                Button(
                    $"Поиск: {messageSearch} · Сбросить",
                    () =>
                        Run(async () =>
                        {
                            messageSearch = "";
                            historyPages = 1;
                            await LoadMessages();
                        })
                )
            );
        if (hasOlder && historyPages < 1000)
            messageList.Children.Add(
                Button(
                    "Показать более ранние сообщения",
                    () =>
                        Run(async () =>
                        {
                            historyPages++;
                            await LoadMessages(showOlder: true);
                        })
                )
            );
        DateTime? previousDate = null;
        foreach (
            var message in messages
                .DistinctBy(message => message.Id)
                .OrderBy(message => message.SentAtUtc)
        )
        {
            var localDate = DateTime
                .SpecifyKind(message.SentAtUtc, DateTimeKind.Utc)
                .ToLocalTime()
                .Date;
            if (localDate != previousDate)
            {
                var day = Text(
                    localDate == DateTime.Today
                        ? "Сегодня"
                        : localDate.ToString(
                            "d MMMM yyyy",
                            System.Globalization.CultureInfo.GetCultureInfo("ru-RU")
                        ),
                    11,
                    "muted"
                );
                day.HorizontalAlignment = HorizontalAlignment.Center;
                messageList.Children.Add(day);
                previousDate = localDate;
            }
            messageList.Children.Add(CreateMessageRow(message));
        }
        if (messages.Count == 0)
            messageList.Children.Add(
                Card(
                    Column(
                        Text("Начните разговор", 18, strong: true),
                        Text("Сообщения и файлы этого чата появятся здесь.", 13, "muted")
                    )
                )
            );
        Dispatcher.UIThread.Post(
            () =>
            {
                if (client != source || selectedChat?.Id != currentChat || request != historyRequest)
                    return;
                if (showOlder)
                    messageScroll.Offset = default;
                else
                    messageScroll.ScrollToEnd();
            },
            DispatcherPriority.Loaded
        );
        await source.Mutate($"api/chats/{currentChat}/read", HttpMethod.Post);
        if (
            client == source
            && chats.FirstOrDefault(chat => chat.Id == currentChat) is { UnreadCount: > 0 } readChat
        )
        {
            readChat.UnreadCount = 0;
            RebuildNavigation();
        }
    }

    private async Task SendMessage()
    {
        if (client is null || selectedChat is null || sending)
            return;
        var text = messageInput.Text ?? "";
        if (string.IsNullOrWhiteSpace(text) && attachments.Count == 0)
            return;
        sending = true;
        UpdateComposer();
        try
        {
            operationId ??= Guid.NewGuid();
            await client.Post<JsonElement>(
                $"api/chats/{selectedChat.Id}/messages",
                new SendMessageRequest
                {
                    Text = text,
                    ClientOperationId = operationId.Value,
                    AttachmentIds = attachments.Select(file => file.Id).ToArray(),
                }
            );
            operationId = null;
            attachments.Clear();
            messageInput.Text = "";
            RefreshAttachments();
            await LoadMessages();
        }
        finally
        {
            sending = false;
            UpdateComposer();
        }
    }

    private async Task UploadFile()
    {
        if (client is null || selectedChat is null || TopLevel.GetTopLevel(this) is not { } top)
            return;
        var files = await top.StorageProvider.OpenFilePickerAsync(
            new() { Title = "Прикрепить файлы", AllowMultiple = true }
        );
        foreach (var file in files)
        {
            await using var stream = await file.OpenReadAsync();
            attachments.Add(
                await client.Upload<FileInfoDto>(
                    $"api/chats/{selectedChat.Id}/files",
                    stream,
                    file.Name
                )
            );
        }
        status.Text = $"Прикреплено файлов: {attachments.Count}";
        RefreshAttachments();
    }

    private async Task SaveFile(FileInfoDto file)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || client is null)
            return;
        var destination = await top.StorageProvider.SaveFilePickerAsync(
            new() { SuggestedFileName = file.Name }
        );
        if (destination is null)
            return;
        var bytes = await client.Download($"api/files/{file.Id}");
        await using var stream = await destination.OpenWriteAsync();
        await stream.WriteAsync(bytes);
    }

    private async Task PreviewCode(FileInfoDto file)
    {
        if (file.Size > 1024 * 1024)
            throw new InvalidOperationException("Предпросмотр кода: до 1 МБ.");
        var bytes = await client!.Download($"api/files/{file.Id}");
        await ShowDialog(
            file.Name,
            new MarkdownView(
                $"```{Path.GetExtension(file.Name).TrimStart('.')}\n{System.Text.Encoding.UTF8.GetString(bytes)}\n```"
            )
        );
    }

    private Task SearchMessages() =>
        ShowTextForm(
            "Поиск сообщений",
            "Текст",
            messageSearch,
            async value =>
            {
                messageSearch = value.Trim();
                historyPages = 1;
                await LoadMessages();
            }
        );

    private Task EditMessage(ChatMessage message) =>
        ShowTextForm(
            "Изменить сообщение",
            "Текст",
            message.Text,
            async value =>
            {
                await client!.Mutate(
                    $"api/messages/{message.Id}",
                    HttpMethod.Patch,
                    new { text = value }
                );
                await LoadMessages();
            }
        );

    private async Task ShowTextForm(
        string title,
        string label,
        string initial,
        Func<string, Task> submit
    )
    {
        var input = Input(label, initial);
        await ShowDialog(
            title,
            Column(
                input,
                Button(
                    "Сохранить",
                    () =>
                        Run(async () =>
                        {
                            await submit(input.Text ?? "");
                            CloseDialog();
                        })
                )
            )
        );
    }

    private Border? dialog;

    private static void Detach(Control control)
    {
        if (control.Parent is Panel panel)
            panel.Children.Remove(control);
        else if (control.Parent is ContentControl content)
            content.Content = null;
        else if (control.Parent is Decorator decorator)
            decorator.Child = null;
    }

    private Task ShowDialog(string title, Control body, double maxWidth = 600)
    {
        CloseDialog();
        if (activeCall is not null)
            activeCall.View.IsVisible = false;
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(Text(title, 22));
        var close = IconButton(
            "Close",
            "Закрыть",
            () =>
            {
                CloseDialog();
                return Task.CompletedTask;
            },
            compact: true
        );
        Grid.SetColumn(close, 1);
        heading.Children.Add(close);
        var card = Card(
            new ScrollViewer
            {
                Content = Column(heading, body),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            }
        );
        card.Bind(
            Border.BackgroundProperty,
            new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("background")
        );
        card.MaxWidth = maxWidth;
        card.MaxHeight = Math.Max(0, Bounds.Height - 40);
        card.Margin = new Thickness(20);
        card.VerticalAlignment = VerticalAlignment.Center;
        card.HorizontalAlignment = HorizontalAlignment.Stretch;
        dialog = new Border
        {
            Background = Brush.Parse("#BB080A10"),
            Child = card,
            ZIndex = 20,
        };
        root.Children.Add(dialog);
        return Task.CompletedTask;
    }

    private void CloseDialog()
    {
        var closed = dialogClosed;
        dialogClosed = null;
        if (dialog is not null)
            root.Children.Remove(dialog);
        dialog = null;
        if (activeCall is not null)
            activeCall.View.IsVisible = true;
        closed?.Invoke();
    }
}
