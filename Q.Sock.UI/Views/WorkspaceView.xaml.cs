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
        StatusText.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBlock.TextProperty)
                StatusText.IsVisible = !string.IsNullOrWhiteSpace(StatusText.Text);
        };
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

    public void SetActions(Control? sidebar) => SidebarActionsHost.Content = sidebar;

    public static bool ShowsMembers(double width, bool? requested) => requested ?? width >= 1200;

    public void SetLayout(
        double width, bool showingConversation, bool directChat, bool? membersOpen = null,
        bool profilePage = false
    )
    {
        var narrow = width < 760;
        var showMembers = showingConversation && !directChat && ShowsMembers(width, membersOpen);
        var dockMembers = showMembers && width >= 1100;
        RailPanel.IsVisible = !narrow;
        SidebarPanel.IsVisible = !narrow || !showingConversation;
        ConversationPanel.IsVisible = !profilePage && (!narrow || showingConversation) && (!showMembers || dockMembers);
        MembersPanel.IsVisible = showMembers;
        BottomBar.IsVisible = narrow && !showingConversation;
        var columns = WorkspaceGrid.ColumnDefinitions;
        columns[0].Width = narrow ? GridLength.Star : new GridLength(72);
        columns[1].Width = new GridLength(narrow ? 0 : 280);
        columns[2].Width = narrow ? new GridLength(0) : GridLength.Star;
        columns[3].Width = new GridLength(dockMembers ? 300 : 0);
        Grid.SetColumn(SidebarPanel, narrow ? 0 : 1);
        Grid.SetColumnSpan(SidebarPanel, profilePage && !narrow ? 2 : 1);
        NavigationPanel.Width = profilePage && !narrow ? Math.Min(540, width - 96) : double.NaN;
        NavigationPanel.HorizontalAlignment = profilePage && !narrow
            ? Avalonia.Layout.HorizontalAlignment.Center
            : Avalonia.Layout.HorizontalAlignment.Stretch;
        Grid.SetColumn(ConversationPanel, narrow ? 0 : 2);
        Grid.SetColumn(MembersPanel, dockMembers ? 3 : narrow ? 0 : 2);
    }
}
