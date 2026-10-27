using Avalonia.Controls;

namespace Q.Sock.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        ViewPreview.Configure(this);
        if (Design.IsDesignMode)
            DataContext = new Q.Sock.UI.ViewModels.SettingsViewModel();
    }
}
