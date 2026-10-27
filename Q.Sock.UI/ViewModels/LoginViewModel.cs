using System.Collections.ObjectModel;

namespace Q.Sock.UI.ViewModels;

public sealed class LoginViewModel : ViewModel
{
    private string serverUrl = "";
    private string userName = "";
    private string password = "";
    private string hint = "Введите логин и пароль выбранного сервера.";
    private bool allowRegistration;

    public LoginViewModel(
        Func<LoginViewModel, Task>? login = null,
        Func<LoginViewModel, Task>? register = null,
        Func<LoginViewModel, Task>? refreshServer = null
    )
    {
        SavedAccounts.CollectionChanged += (_, _) => Notify(nameof(HasSavedAccounts));
        LoginCommand = new(() => login?.Invoke(this) ?? Task.CompletedTask);
        RegisterCommand = new(() => register?.Invoke(this) ?? Task.CompletedTask);
        RefreshServerCommand = new(() => refreshServer?.Invoke(this) ?? Task.CompletedTask);
    }

    public string ServerUrl
    {
        get => serverUrl;
        set => Set(ref serverUrl, value ?? "");
    }
    public string UserName
    {
        get => userName;
        set => Set(ref userName, value ?? "");
    }
    public string Password
    {
        get => password;
        set => Set(ref password, value ?? "");
    }
    public string Hint
    {
        get => hint;
        set => Set(ref hint, value);
    }
    public bool AllowRegistration
    {
        get => allowRegistration;
        set => Set(ref allowRegistration, value);
    }
    public bool ShowServer { get; init; } = true;
    public string Notice { get; init; } = "";
    public bool HasSavedAccounts => SavedAccounts.Count > 0;
    public bool ShowNotice => !string.IsNullOrWhiteSpace(Notice);
    public ObservableCollection<SettingsAction> SavedAccounts { get; } = [];
    public AsyncCommand LoginCommand { get; }
    public AsyncCommand RegisterCommand { get; }
    public AsyncCommand RefreshServerCommand { get; }
}

public sealed record SettingsAction(string Label, AsyncCommand Command);
