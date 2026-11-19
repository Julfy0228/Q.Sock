using System.Net.Http;
using Q.Sock.Core;
using Q.Sock.Shared.Models;
using Q.Sock.UI.ViewModels;
using Q.Sock.UI.Views;

namespace Q.Sock.UI;

public sealed partial class MessengerView
{
    private LoginView? loginView;
    private Func<Task>? refreshLoginIdentity;

    private void ShowLogin()
    {
        showingLogin = true;
        CloseDialog();
        root.Children.Clear();
        var model = new LoginViewModel(
            login: input =>
                Run(() => Authenticate(input.ServerUrl, input.UserName, input.Password, false)),
            register: input =>
                Run(() => Authenticate(input.ServerUrl, input.UserName, input.Password, true)),
            refreshServer: RefreshLoginServer,
            help: ShowDocumentation
        )
        {
            ServerUrl = browserOrigin ?? account?.ServerUrl ?? "http://127.0.0.1:5210",
            ShowServer = browserOrigin is null,
            AllowRegistration = serverInfo?.Policy.AllowRegistration == true,
            Notice = status.Text is { Length: > 0 } message && message != "Q.Sock" ? message : "",
        };
        foreach (var saved in accounts)
            model.SavedAccounts.Add(
                new(saved.ToString(), new(() => Run(() => ConnectAccount(saved))))
            );
        loginView = new LoginView { DataContext = model };
        loginView.SetViewport(Bounds.Size);
        root.Children.Add(loginView);
        refreshLoginIdentity = () => model.RefreshServerCommand.ExecuteAsync();
    }

    private async Task RefreshLoginServer(LoginViewModel model)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await using var transport = new MessengerClient(model.ServerUrl);
            var info = await transport.PublicGet<ServerInfo>("api/server", timeout.Token);
            if (!showingLogin || loginView?.DataContext != model)
                return;
            serverInfo = info;
            model.Hint = "Введите логин и пароль выбранного сервера.";
            model.AllowRegistration = info.Policy.AllowRegistration;
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or System.Net.WebException
                        or OperationCanceledException
                        or ArgumentException
            )
        {
            if (!showingLogin || loginView?.DataContext != model)
                return;
            model.Hint = "Сервер недоступен. Проверьте адрес подключения.";
            model.AllowRegistration = false;
        }
    }
}
