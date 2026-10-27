using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Q.Sock.UI.ViewModels;

namespace Q.Sock.UI.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        ViewPreview.Configure(this);
        if (Design.IsDesignMode)
            DataContext = new LoginViewModel();
        SizeChanged += (_, _) => SetViewport(Bounds.Size);
        AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(() => UserNameInput.Focus(), DispatcherPriority.Input);
    }

    public void SetViewport(Size size)
    {
        Viewport.MinHeight = Math.Max(0, size.Height);
        Form.Width = Math.Min(342, Math.Max(0, size.Width - 48));
    }

    private async void ServerLostFocus(object? sender, RoutedEventArgs args)
    {
        if (DataContext is LoginViewModel model)
            await model.RefreshServerCommand.ExecuteAsync();
    }

    private async void PasswordKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || DataContext is not LoginViewModel model)
            return;
        args.Handled = true;
        await model.LoginCommand.ExecuteAsync();
    }
}
