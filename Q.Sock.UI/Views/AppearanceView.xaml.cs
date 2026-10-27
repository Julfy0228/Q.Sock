using Avalonia.Controls;

namespace Q.Sock.UI.Views;

public partial class AppearanceView : UserControl
{
    public AppearanceView()
    {
        InitializeComponent();
        ViewPreview.Configure(this);
        if (Design.IsDesignMode)
            DataContext = new Q.Sock.UI.ViewModels.AppearanceViewModel(
                new Q.Sock.Core.MessengerTheme(),
                _ => Task.CompletedTask,
                () => Task.CompletedTask
            );
    }
}
