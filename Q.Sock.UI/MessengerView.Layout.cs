using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Q.Sock.Core;
using Q.Sock.Shared.Models;
using static Q.Sock.UI.UiFactory;

namespace Q.Sock.UI;

public sealed partial class MessengerView
{
    private readonly Grid shell = new() { RowDefinitions = new RowDefinitions("72,*,Auto") };
    private readonly StackPanel railItems = new() { Spacing = 16, Margin = new Thickness(12, 14) };
    private readonly Grid sidebarHeader = new()
    {
        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        Margin = new Thickness(16),
    };
    private readonly TextBlock sidebarTitle = Text("Чаты", 18, strong: true);
    private readonly TextBox navigationSearch = Input("Поиск людей и сообщений");
    private readonly StackPanel memberList = new() { Spacing = 20, Margin = new Thickness(20) };
    private readonly Grid bottomNavigation = new()
    {
        ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
    };
    private Border? desktopHeader;
    private Border? rail;
    private Border? sidebar;
    private Border? membersPanel;
    private Border? bottomBar;
    private Border? searchSurface;
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
        loginSurface = null;
        loginForm = null;
        root.Children.Clear();
        workspace.Children.Clear();
        shell.Children.Clear();
        sidebarHeader.Children.Clear();
        Detach(sidebarTitle);
        Detach(status);
        Detach(navigation);
        Detach(conversation);
        Detach(navigationSearch);
        Detach(bottomNavigation);
        Detach(railItems);
        Detach(memberList);
        var title = Column(
            Text("Мессенджер", 18, strong: true),
            Text($"{DisplayName} · {client!.Server.Host}", 11, "muted")
        );
        title.Spacing = 0;
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(16, 10),
        };
        header.Children.Add(title);
        var options = NavigationMenu();
        Grid.SetColumn(options, 1);
        header.Children.Add(options);
        desktopHeader = Surface(header, "panel");
        shell.Children.Add(desktopHeader);
        rail = Surface(new ScrollViewer { Content = railItems }, "chrome");
        workspace.Children.Add(rail);
        sidebarHeader.Children.Add(Column(sidebarTitle, status));
        status.FontSize = 11;
        status.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("muted"));
        var sidebarOptions = NavigationMenu();
        Grid.SetColumn(sidebarOptions, 1);
        sidebarHeader.Children.Add(sidebarOptions);
        navigationSearch.Classes.Add("search");
        navigationSearch.TextChanged -= NavigationSearchChanged;
        navigationSearch.TextChanged += NavigationSearchChanged;
        var search = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*") };
        var searchIcon = new MaterialIcon("Search", 22)
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        search.Children.Add(searchIcon);
        Grid.SetColumn(navigationSearch, 1);
        search.Children.Add(navigationSearch);
        searchSurface = Surface(search, "surface", new Thickness(8, 0), 8);
        searchSurface.Margin = new Thickness(12, 0, 12, 4);
        var side = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        side.Children.Add(sidebarHeader);
        Grid.SetRow(searchSurface, 1);
        side.Children.Add(searchSurface);
        var list = new ScrollViewer
        {
            Content = navigation,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(list, 2);
        side.Children.Add(list);
        sidebar = Surface(side, "panel");
        Grid.SetColumn(sidebar, 1);
        workspace.Children.Add(sidebar);
        Grid.SetColumn(conversation, 2);
        workspace.Children.Add(conversation);
        membersPanel = Surface(new ScrollViewer { Content = memberList }, "panel");
        Grid.SetColumn(membersPanel, 3);
        workspace.Children.Add(membersPanel);
        Grid.SetRow(workspace, 1);
        shell.Children.Add(workspace);
        bottomBar = Surface(bottomNavigation, "chrome", new Thickness(4, 4));
        Grid.SetRow(bottomBar, 2);
        shell.Children.Add(bottomBar);
        root.Children.Add(shell);
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
            if (loginSurface is not null)
                loginSurface.MinHeight = Math.Max(0, Bounds.Height);
            if (loginForm is not null)
                loginForm.Width = Math.Min(342, Math.Max(0, Bounds.Width - 48));
            if (dialog?.Child is Border card)
                card.MaxHeight = Math.Max(0, Bounds.Height - 32);
            if (
                sidebar is null
                || rail is null
                || membersPanel is null
                || bottomBar is null
                || desktopHeader is null
            )
                return;
            var narrow = Bounds.Width < 760;
            var showMembers =
                Bounds.Width >= 1200 && showConversation && selectedChat?.Type != "direct";
            shell.RowDefinitions = new RowDefinitions(narrow ? "0,*,Auto" : "72,*,0");
            desktopHeader.IsVisible = !narrow;
            rail.IsVisible = !narrow;
            sidebar.IsVisible = !narrow || !showConversation;
            conversation.IsVisible = !narrow || showConversation;
            membersPanel.IsVisible = showMembers;
            bottomBar.IsVisible = narrow && !showConversation;
            workspace.ColumnDefinitions = new ColumnDefinitions(
                narrow ? "*"
                : showMembers ? "76,280,*,280"
                : "64,280,*"
            );
            Grid.SetColumn(sidebar, narrow ? 0 : 1);
            Grid.SetColumn(conversation, narrow ? 0 : 2);
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
