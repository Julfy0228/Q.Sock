using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.AspNetCore.SignalR.Client;
using Q.Sock.Shared;

namespace Q.Sock.Client;

public partial class MainWindow : Window
{
    private HubConnection? _connection;

    public ObservableCollection<MessageModel> Messages { get; } = [];
    public ObservableCollection<string> Users { get; } = [];

    private const string ServerUrl = "http://localhost:5000/chat";

    public MainWindow()
    {
        InitializeComponent();
        MessagesListBox.ItemsSource = Messages;
        UsersListBox.ItemsSource = Users;
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
        {
            MessageBox.Show("Введите никнейм!");
            return;
        }

        ConnectButton.IsEnabled = false;
        UsernameTextBox.IsEnabled = false;

        _connection = new HubConnectionBuilder()
            .WithUrl(ServerUrl)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<MessageModel>("ReceiveMessage", (message) =>
        {
            Dispatcher.Invoke(() => Messages.Add(message));
        });

        _connection.On<List<MessageModel>>("ReceiveHistory", (history) =>
        {
            Dispatcher.Invoke(() =>
            {
                Messages.Clear();
                foreach (var msg in history)
                    Messages.Add(msg);
            });
        });

        _connection.On<List<string>>("UpdateUsersList", (usersList) =>
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
            await _connection.InvokeAsync("RegisterUser", UsernameTextBox.Text);

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
            UsernameTextBox.IsEnabled = true;
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