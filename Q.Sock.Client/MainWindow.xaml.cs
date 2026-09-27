using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.AspNetCore.SignalR.Client;
using Q.Sock.Shared.Models.Auth;
using Q.Sock.Shared.Models;

namespace Q.Sock.Client;

public partial class MainWindow : Window
{
    private HubConnection? _connection;
    private string? _accessToken;

    public ObservableCollection<Message> Messages { get; } = [];
    public ObservableCollection<User> Users { get; } = [];

    private const string ServerUrl = "http://localhost:5000/chat";
    private const string ApiUrl = "http://localhost:5000";

    public MainWindow()
    {
        InitializeComponent();
        MessagesListBox.ItemsSource = Messages;
        UsersListBox.ItemsSource = Users;
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        await AuthenticateAsync(isRegistration: false);
    }

    private async void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        await AuthenticateAsync(isRegistration: true);
    }

    private async Task AuthenticateAsync(bool isRegistration)
    {
        if (string.IsNullOrWhiteSpace(UsernameTextBox.Text) || string.IsNullOrWhiteSpace(PasswordTextBox.Password))
        {
            MessageBox.Show("Введите логин и пароль!");
            return;
        }

        ConnectButton.IsEnabled = false;
        RegisterButton.IsEnabled = false;
        UsernameTextBox.IsEnabled = false;
        PasswordTextBox.IsEnabled = false;

        try
        {
            using var httpClient = new HttpClient();
            HttpResponseMessage response;
            if (isRegistration)
            {
                response = await httpClient.PostAsJsonAsync($"{ApiUrl}/api/auth/register", new RegisterRequest
                {
                    UserName = UsernameTextBox.Text,
                    Password = PasswordTextBox.Password
                });
            }
            else
            {
                response = await httpClient.PostAsJsonAsync($"{ApiUrl}/api/auth/login", new LoginRequest
                {
                    UserName = UsernameTextBox.Text,
                    Password = PasswordTextBox.Password
                });
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(body) ? "Ошибка сервера." : body);
            }

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>()
                ?? throw new InvalidOperationException("Сервер не вернул токен.");

            _accessToken = auth.AccessToken;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка авторизации: {ex.Message}");
            ConnectButton.IsEnabled = true;
            RegisterButton.IsEnabled = true;
            UsernameTextBox.IsEnabled = true;
            PasswordTextBox.IsEnabled = true;
            return;
        }

        _connection = new HubConnectionBuilder()
            .WithUrl(ServerUrl, options => options.AccessTokenProvider = () => Task.FromResult<string?>(_accessToken))
            .WithAutomaticReconnect()
            .Build();

        _connection.On<Message>("ReceiveMessage", (message) =>
        {
            Dispatcher.Invoke(() => Messages.Add(message));
        });

        _connection.On<List<Message>>("ReceiveHistory", (history) =>
        {
            Dispatcher.Invoke(() =>
            {
                Messages.Clear();
                foreach (var msg in history)
                    Messages.Add(msg);
            });
        });

        _connection.On<List<User>>("UpdateUsersList", (usersList) =>
        {
            Dispatcher.Invoke(() =>
            {
                Users.Clear();
                foreach (var user in usersList)
                    Users.Add(user);
            });
        });

        try
        {
            await _connection.StartAsync();

            StatusTextBlock.Text = "Подключено";
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
            MessageTextBox.IsEnabled = true;
            SendButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка подключения к серверу: {ex.Message}");
            StatusTextBlock.Text = "Ошибка";
            ConnectButton.IsEnabled = true;
            RegisterButton.IsEnabled = true;
            UsernameTextBox.IsEnabled = true;
            PasswordTextBox.IsEnabled = true;
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendMessage();
    }

    private async void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SendMessage();
        }
    }

    private async Task SendMessage()
    {
        if (_connection == null || _connection.State != HubConnectionState.Connected)
            return;

        if (string.IsNullOrWhiteSpace(MessageTextBox.Text))
            return;

        try
        {
            await _connection.InvokeAsync("SendMessage", MessageTextBox.Text);
            MessageTextBox.Clear();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка отправки: {ex.Message}");
        }
    }

    protected override async void OnClosed(EventArgs e)
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }
        base.OnClosed(e);
    }
}