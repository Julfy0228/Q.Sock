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
    private bool? membersOpen;

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
        status.Text = "";
        navigationSearch.TextChanged -= NavigationSearchChanged;
        navigationSearch.TextChanged += NavigationSearchChanged;
        root.Children.Add(workspaceView);
        ShowEmptyConversation("Выберите чат", "");
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
            if (documentation is not null)
                documentation.Height = Math.Max(200, Bounds.Height - 210);
            if (dialog?.Child is Border card)
                card.MaxHeight = Math.Max(0, Bounds.Height - 32);
            var narrow = Bounds.Width < 760;
            workspaceView.SetLayout(
                Bounds.Width, showConversation, selectedChat?.Type == "direct", membersOpen,
                profilePage: navigationSection == "Профиль"
            );
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
        var content = Column(Text(title, 24, strong: true));
        if (description.Length != 0)
            content.Children.Add(Text(description, 14, "secondary"));
        content.MaxWidth = 360;
        content.Margin = new Thickness(28);
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        conversation.Children.Add(content);
    }

    private Control? WorkspaceActions()
    {
        if (selectedGuild is not null)
            return Row(
                IconButton("SettingsOutlined", "Настройки пространства", () => Run(ShowSpace), compact: true),
                IconButton("Add", "Новый канал", () => Run(CreateChat), compact: true)
            );
        return navigationSection switch
        {
            "Чаты" => IconButton("Add", "Новый чат", () => Run(CreateChat), compact: true),
            "Пространства" => IconButton("Add", "Создать пространство", () => Run(CreateGuild), compact: true),
            _ => null,
        };
    }
}
