using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Q.Sock.Shared.Models;
using static Q.Sock.UI.UiFactory;

namespace Q.Sock.UI;

public sealed partial class MessengerView
{
    private StackPanel railItems => workspaceView.RailItems;
    private TextBlock sidebarTitle => workspaceView.SidebarTitle;
    private TextBox navigationSearch => workspaceView.Search;
    private StackPanel memberList => workspaceView.Members;
    private Grid bottomNavigation => workspaceView.BottomNavigation;
    private Border searchSurface => workspaceView.SearchBackground;
    private string navigationSection = "Чаты";
    private CallInfo[] availableCalls = [];
    private MemberInfo[] conversationMembers = [];
    private bool composingLayout;

    private string DisplayName =>
        string.IsNullOrWhiteSpace(profile?.DisplayName)
            ? account?.UserName ?? "Пользователь"
            : profile.DisplayName;

    private void BuildWorkspace()
    {
        showingLogin = false;
        loginView = null;
        root.Children.Clear();
        workspace.IsVisible = true;
        workspaceView.SetIdentity($"{DisplayName} · {client!.Server.Host}");
        workspaceView.SetActions(NavigationMenu(), NavigationMenu());
        navigationSearch.TextChanged -= NavigationSearchChanged;
        navigationSearch.TextChanged += NavigationSearchChanged;
        root.Children.Add(workspaceView);
        ShowEmptyConversation(
            "Выберите чат",
            "Личные и групповые чаты, каналы и голосовые комнаты."
        );
        RebuildNavigation();
        UpdateLayoutMode();
    }

    private void NavigationSearchChanged(object? sender, TextChangedEventArgs args) =>
        RebuildNavigation();

    private void UpdateLayoutMode()
    {
        if (composingLayout)
            return;
        composingLayout = true;
        try
        {
            UpdateCallLayout();
            loginView?.SetViewport(Bounds.Size);
            if (dialog?.Child is Border card)
                card.MaxHeight = Math.Max(0, Bounds.Height - 32);
            var narrow = Bounds.Width < 760;
            workspaceView.SetLayout(Bounds.Width, showConversation, selectedChat?.Type == "direct");
            messageList.Margin = new Thickness(narrow ? 16 : 28, 20);
            if (conversationHeaderTitle is not null)
                conversationHeaderTitle.FontSize = narrow ? 18 : 24;
            if (conversationBack is not null)
                conversationBack.IsVisible = narrow;
        }
        finally
        {
            composingLayout = false;
        }
    }

    private void ShowEmptyConversation(string title, string description)
    {
        conversation.Children.Clear();
        var content = Column(
            Avatar("М", 72),
            Text(title, 24, strong: true),
            Text(description, 14, "secondary")
        );
        content.MaxWidth = 360;
        content.Margin = new Thickness(28);
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        conversation.Children.Add(content);
    }

    private Button NavigationMenu()
    {
        var button = IconButton("MoreHoriz", "Действия", () => Task.CompletedTask, compact: true);
        var menu = new ContextMenu();
        void AddAction(string label, Func<Task> action)
        {
            var item = new MenuItem { Header = label };
            item.Click += async (_, _) => await Run(action);
            menu.Items.Add(item);
        }
        AddAction("Новый чат или канал", CreateChat);
        AddAction("Создать пространство", CreateGuild);
        AddAction("Люди и друзья", ShowContacts);
        if (selectedGuild is not null)
            AddAction("Настройки пространства", ShowSpace);
        AddAction("Настройки и аккаунты", ShowSettings);
        AddAction("Модули интерфейса", ShowClientPlugins);
        foreach (
            var plugin in externalPlugins.Values.Where(plugin =>
                plugin.Package.Slot is "action" or "workspace"
            )
        )
        {
            var current = plugin;
            AddAction(current.Package.Title, () => OpenClientPlugin(current));
        }
        button.ContextMenu = menu;
        button.Click += (_, _) => menu.Open(button);
        return button;
    }
}
