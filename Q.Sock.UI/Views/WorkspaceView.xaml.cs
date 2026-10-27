using Avalonia;
using Avalonia.Controls;

namespace Q.Sock.UI.Views;

public partial class WorkspaceView : UserControl
{
    public WorkspaceView()
    {
        InitializeComponent();
        ViewPreview.Configure(this);
        SearchIconHost.Content = new MaterialIcon("Search", 22);
        if (Design.IsDesignMode)
            SizeChanged += (_, _) => SetLayout(Bounds.Width, false, true);
    }

    public Grid Workspace => WorkspaceGrid;
    public StackPanel RailItems => RailItemsPanel;
    public StackPanel Navigation => NavigationPanel;
    public StackPanel Members => MemberListPanel;
    public Grid Conversation => ConversationPanel;
    public TextBlock Status => StatusText;
    public TextBlock SidebarTitle => SidebarTitleText;
    public TextBox Search => SearchInput;
    public Grid BottomNavigation => BottomNavigationGrid;
    public Border SearchBackground => SearchSurface;

    public void SetIdentity(string value) => IdentityText.Text = value;

    public void SetActions(Control desktop, Control sidebar)
    {
        DesktopActionsHost.Content = desktop;
        SidebarActionsHost.Content = sidebar;
    }

    public void SetLayout(double width, bool showingConversation, bool directChat)
    {
        var narrow = width < 760;
        var showMembers = width >= 1200 && showingConversation && !directChat;
        ShellGrid.RowDefinitions = new RowDefinitions(narrow ? "0,*,Auto" : "72,*,0");
        DesktopHeader.IsVisible = !narrow;
        RailPanel.IsVisible = !narrow;
        SidebarPanel.IsVisible = !narrow || !showingConversation;
        ConversationPanel.IsVisible = !narrow || showingConversation;
        MembersPanel.IsVisible = showMembers;
        BottomBar.IsVisible = narrow && !showingConversation;
        WorkspaceGrid.ColumnDefinitions = new ColumnDefinitions(
            narrow ? "*"
            : showMembers ? "76,280,*,280"
            : "64,280,*"
        );
        Grid.SetColumn(SidebarPanel, narrow ? 0 : 1);
        Grid.SetColumn(ConversationPanel, narrow ? 0 : 2);
    }
}
