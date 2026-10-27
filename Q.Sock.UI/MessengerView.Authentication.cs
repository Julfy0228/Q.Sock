using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Q.Sock.Core;
using Q.Sock.Shared.Models;
using static Q.Sock.UI.UiFactory;

namespace Q.Sock.UI;

public sealed partial class MessengerView
{
    private Grid? loginSurface;
    private StackPanel? loginForm;
    private Func<Task>? refreshLoginIdentity;

    private void ShowLogin()
    {
        showingLogin = true;
        CloseDialog();
        root.Children.Clear();
        var server = Input("Адрес сервера", account?.ServerUrl ?? "http://127.0.0.1:5210");
        server.IsVisible = browserOrigin is null;
        if (browserOrigin is not null)
            server.Text = browserOrigin;
        var username = Input("Логин");
        var password = Input("Пароль");
        password.PasswordChar = '●';
        username.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(() => username.Focus(), DispatcherPriority.Input);
        password.KeyDown += async (_, args) =>
        {
            if (args.Key != Key.Enter)
                return;
            args.Handled = true;
            await Run(() => Authenticate(server.Text!, username.Text!, password.Text!, false));
        };
        var hint = Text("Введите логин и пароль выбранного сервера.", 12, "muted");
        var identity = Text("Учётная запись сервера", 14, strong: true);
        var form = new StackPanel
        {
            Spacing = 18,
            Width = Math.Min(342, Math.Max(240, Bounds.Width - 48)),
            MaxWidth = 342,
            Margin = new Thickness(24, 36),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        loginForm = form;
        form.Children.Add(Avatar("М", 58));
        form.Children.Add(Text("Мессенджер", 28, strong: true));
        form.Children.Add(Text("Личные чаты, группы и пространства", 14, "secondary"));
        form.Children.Add(new Border { Height = 12 });
        form.Children.Add(Text("Вход", 16, strong: true));
        form.Children.Add(hint);
        form.Children.Add(new Separator());
        form.Children.Add(identity);
        if (browserOrigin is null)
            form.Children.Add(Field("Сервер", server));
        form.Children.Add(Field("Логин", username));
        form.Children.Add(Field("Пароль", password));
        var login = Primary(
            "Войти",
            () => Run(() => Authenticate(server.Text!, username.Text!, password.Text!, false))
        );
        login.HorizontalAlignment = HorizontalAlignment.Center;
        login.Margin = new Thickness(0, 4, 0, 0);
        form.Children.Add(login);
        var register = Button(
            "Создать аккаунт",
            () => Run(() => Authenticate(server.Text!, username.Text!, password.Text!, true))
        );
        register.Classes.Add("quiet");
        register.HorizontalAlignment = HorizontalAlignment.Center;
        register.IsVisible = serverInfo?.Policy.AllowRegistration == true;
        form.Children.Add(register);
        form.Children.Add(
            Text("Проблема со входом? Обратитесь к администратору сервера", 12, "muted")
        );
        refreshLoginIdentity = async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await using var transport = new MessengerClient(server.Text ?? "");
                var info = await transport.PublicGet<ServerInfo>("api/server", timeout.Token);
                if (!showingLogin || loginForm != form)
                    return;
                serverInfo = info;
                hint.Text = "Введите логин и пароль выбранного сервера.";
                register.IsVisible = info.Policy.AllowRegistration;
            }
            catch (Exception exception)
                when (exception
                        is HttpRequestException
                            or OperationCanceledException
                            or ArgumentException
                )
            {
                hint.Text = "Сервер недоступен. Проверьте адрес подключения.";
                register.IsVisible = false;
            }
        };
        server.LostFocus += async (_, _) => await refreshLoginIdentity();
        foreach (var saved in accounts)
        {
            var accountButton = Button(saved.ToString(), () => Run(() => ConnectAccount(saved)));
            accountButton.Classes.Add("quiet");
            form.Children.Add(accountButton);
        }
        if (!string.IsNullOrWhiteSpace(status.Text) && status.Text != "Q.Sock")
            form.Children.Add(Text(status.Text, 12, "secondary"));
        loginSurface = new Grid { MinHeight = Math.Max(0, Bounds.Height) };
        loginSurface.Children.Add(form);
        root.Children.Add(
            new ScrollViewer
            {
                Content = loginSurface,
                HorizontalScrollBarVisibility = Avalonia
                    .Controls
                    .Primitives
                    .ScrollBarVisibility
                    .Disabled,
            }
        );
    }
}
