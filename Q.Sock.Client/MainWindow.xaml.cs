using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.AspNetCore.SignalR.Client;
using Q.Sock.Client.Services;
using Q.Sock.Shared.Models;
using Q.Sock.Shared.Models.Auth;

namespace Q.Sock.Client;

public partial class MainWindow : Window
{
    private const string ServerUrl = "http://localhost:5000/chat";
    private const string ApiUrl = "http://localhost:5000";

    private readonly AccountStore _accountStore = new();
    private readonly HttpClient _httpClient = new();
    private readonly ObservableCollection<SavedAccount> _accounts = [];
    private HubConnection? _connection;
    private SavedAccount? _currentAccount;

    public ObservableCollection<Message> Messages { get; } = [];
    public ObservableCollection<User> Users { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        MessagesItemsControl.ItemsSource = Messages;
        UsersItemsControl.ItemsSource = Users;
        AccountsItemsControl.ItemsSource = _accounts;
        AddHandler(InputElement.PointerPressedEvent, Window_PointerPressed, RoutingStrategies.Tunnel);
        Opened += MainWindow_Opened;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        foreach (var account in await _accountStore.LoadAsync())
            _accounts.Add(account);

        var validAccount = _accounts.FirstOrDefault(account => account.ExpiresAtUtc > DateTime.UtcNow);
        if (validAccount is not null)
            await ConnectWithAccountAsync(validAccount);
    }

    private async void LoginButton_Click(object? sender, RoutedEventArgs e) =>
        await AuthenticateAsync(isRegistration: false);

    private async void RegisterButton_Click(object? sender, RoutedEventArgs e) =>
        await AuthenticateAsync(isRegistration: true);

    private void ToggleAccountsButton_Click(object? sender, RoutedEventArgs e)
    {
        AccountsPopup.IsVisible = !AccountsPopup.IsVisible;
        LoginPanel.IsVisible = false;
    }

    private void ShowLoginPanel_Click(object? sender, RoutedEventArgs e)
    {
        AccountsPopup.IsVisible = false;
        SetAuthenticationControlsEnabled(true);
        LoginPanel.IsVisible = true;
    }

    private void HideLoginPanel_Click(object? sender, RoutedEventArgs e)
    {
        LoginPanel.IsVisible = false;
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || (!AccountsPopup.IsVisible && !LoginPanel.IsVisible))
            return;

        AccountsPopup.IsVisible = false;
        LoginPanel.IsVisible = false;
        e.Handled = true;
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!AccountsPopup.IsVisible && !LoginPanel.IsVisible)
            return;

        if (PointerIsInside(e, AccountsPopup) ||
            PointerIsInside(e, LoginPanel) ||
            PointerIsInside(e, ProfileButton))
            return;

        AccountsPopup.IsVisible = false;
        LoginPanel.IsVisible = false;
    }

    private static bool PointerIsInside(PointerPressedEventArgs e, Control control)
    {
        if (!control.IsVisible)
            return false;

        var bounds = new Rect(0, 0, control.Bounds.Width, control.Bounds.Height);
        return bounds.Contains(e.GetPosition(control));
    }

    private async void LogoutButton_Click(object? sender, RoutedEventArgs e)
    {
        await DisconnectAsync();
        AccountsPopup.IsVisible = false;
    }

    private void ToggleOnlineButton_Click(object? sender, RoutedEventArgs e)
    {
        OnlinePanel.IsVisible = !OnlinePanel.IsVisible;
    }

    private void UpdateProfileWidget()
    {
        if (_currentAccount is not null)
        {
            CurrentUserName.Text = _currentAccount.UserName;
            CurrentUserInitial.Text = _currentAccount.UserName[0].ToString().ToUpperInvariant();
            CurrentUserStatus.Text = _connection?.State == HubConnectionState.Connected ? "Онлайн" : "Подключение...";
        }
        else
        {
            CurrentUserName.Text = "Не подключено";
            CurrentUserInitial.Text = "?";
            CurrentUserStatus.Text = "Оффлайн";
        }
    }

    private async Task AuthenticateAsync(bool isRegistration)
    {
        var userName = UserNameTextBox.Text?.Trim() ?? string.Empty;
        var password = PasswordTextBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            await ShowMessageAsync("Введите логин и пароль.");
            return;
        }

        SetAuthenticationControlsEnabled(false);
        try
        {
            HttpResponseMessage response;
            if (isRegistration)
            {
                response = await _httpClient.PostAsJsonAsync($"{ApiUrl}/api/auth/register", new RegisterRequest
                {
                    UserName = userName,
                    Password = password
                });
            }
            else
            {
                response = await _httpClient.PostAsJsonAsync($"{ApiUrl}/api/auth/login", new LoginRequest
                {
                    UserName = userName,
                    Password = password
                });
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(isRegistration
                    ? "Не удалось создать аккаунт. Возможно, такой логин уже занят."
                    : "Неверный логин или пароль.");
            }

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>()
                ?? throw new InvalidOperationException("Сервер не вернул JWT.");

            var account = new SavedAccount
            {
                UserName = userName,
                AccessToken = auth.AccessToken,
                ExpiresAtUtc = auth.ExpiresAtUtc
            };

            await SaveOrUpdateAccountAsync(account);
            await ConnectWithAccountAsync(account);
            LoginPanel.IsVisible = false;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Ошибка авторизации: {ex.Message}");
            SetAuthenticationControlsEnabled(true);
        }
    }

    private async void SwitchAccountButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SavedAccount account })
            return;

        if (account == _currentAccount)
        {
            AccountsPopup.IsVisible = false;
            return;
        }

        if (account.ExpiresAtUtc <= DateTime.UtcNow)
        {
            await ShowMessageAsync("Срок действия токена истёк. Войдите в этот аккаунт снова.");
            return;
        }

        AccountsPopup.IsVisible = false;
        await ConnectWithAccountAsync(account);
    }

    private async void RemoveSavedAccountButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SavedAccount account })
            return;

        if (account == _currentAccount)
            await DisconnectAsync();

        _accounts.Remove(account);
        await PersistAccountsAsync();
    }

    private async Task ConnectWithAccountAsync(SavedAccount account)
    {
        await DisconnectAsync();
        SetAuthenticationControlsEnabled(false);
        StatusTextBlock.Text = $"Подключение: {account.UserName}";

        var connection = new HubConnectionBuilder()
            .WithUrl(ServerUrl, options =>
                options.AccessTokenProvider = () => Task.FromResult<string?>(account.AccessToken))
            .WithAutomaticReconnect()
            .Build();

        connection.On<Message>("ReceiveMessage", message =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Messages.Add(message)));

        connection.On<IEnumerable<Message>>("ReceiveHistory", history =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Messages.Clear();
                foreach (var message in history)
                    Messages.Add(message);
            }));

        connection.On<IEnumerable<User>>("UpdateUsersList", users =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Users.Clear();
                foreach (var user in users)
                    Users.Add(user);
            }));

        connection.Closed += async _ =>
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_connection != connection)
                    return;

                StatusTextBlock.Text = "Соединение закрыто";
                MessageTextBox.IsEnabled = false;
                SendButton.IsEnabled = false;
                UpdateProfileWidget();
            });
        };

        Exception? connectionError = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await connection.StartAsync();
                connectionError = null;
                break;
            }
            catch (Exception ex)
            {
                connectionError = ex;
                if (attempt == 5)
                    break;

                StatusTextBlock.Text = $"Сервер недоступен. Повторная попытка {attempt + 1}/5...";
                await Task.Delay(TimeSpan.FromSeconds(attempt));
            }
        }

        if (connectionError is not null)
        {
            await connection.DisposeAsync();
            SetAuthenticationControlsEnabled(true);
            StatusTextBlock.Text = "Ошибка подключения";
            await ShowMessageAsync($"Не удалось подключиться к чату после 5 попыток: {connectionError.Message}");
            return;
        }

        _connection = connection;
        _currentAccount = account;
        Messages.Clear();
        StatusTextBlock.Text = $"Подключено как {account.UserName}";
        MessageTextBox.IsEnabled = true;
        SendButton.IsEnabled = true;
        SetAuthenticationControlsEnabled(true);
        UpdateProfileWidget();
    }

    private async void SendButton_Click(object? sender, RoutedEventArgs e) => await SendMessageAsync();

    private async void MessageTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await SendMessageAsync();
    }

    private async Task SendMessageAsync()
    {
        var text = MessageTextBox.Text?.Trim() ?? string.Empty;
        if (_connection?.State != HubConnectionState.Connected || string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            await _connection.InvokeAsync("SendMessage", text);
            MessageTextBox.Text = string.Empty;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Ошибка отправки: {ex.Message}");
        }
    }

    private async Task DisconnectAsync()
    {
        var connection = _connection;
        _connection = null;
        _currentAccount = null;
        Users.Clear();
        MessageTextBox.IsEnabled = false;
        SendButton.IsEnabled = false;
        StatusTextBlock.Text = "Не подключено";
        UpdateProfileWidget();

        if (connection is not null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await connection.StopAsync(timeout.Token);
            }
            catch
            {
                // Disconnect still disposes the connection if the server does not respond.
            }

            try
            {
                await connection.DisposeAsync();
            }
            catch
            {
                // The connection may already have been disposed by SignalR.
            }
        }
    }

    private async Task SaveOrUpdateAccountAsync(SavedAccount account)
    {
        var existing = _accounts.FirstOrDefault(item =>
            string.Equals(item.UserName, account.UserName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            _accounts.Remove(existing);

        _accounts.Insert(0, account);
        await PersistAccountsAsync();
    }

    private async Task PersistAccountsAsync() => await _accountStore.SaveAsync(_accounts);

    private void SetAuthenticationControlsEnabled(bool enabled)
    {
        LoginButton.IsEnabled = enabled;
        RegisterButton.IsEnabled = enabled;
        UserNameTextBox.IsEnabled = enabled;
        PasswordTextBox.IsEnabled = enabled;
    }

    private async Task ShowMessageAsync(string message)
    {
        var closeButton = new Button
        {
            Content = "Закрыть",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        var dialog = new Window
        {
            Title = "Q.Sock",
            Width = 420,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    closeButton
                }
            }
        };

        closeButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        await DisconnectAsync();
        _httpClient.Dispose();
    }
}
