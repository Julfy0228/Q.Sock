using System.Collections.ObjectModel;

namespace Q.Sock.UI.ViewModels;

public sealed class SettingsViewModel : ViewModel
{
    private bool allowRotation;

    public SettingsViewModel()
    {
        Accounts.CollectionChanged += (_, _) => Notify(nameof(HasAccounts));
        Sessions.CollectionChanged += (_, _) => Notify(nameof(HasSessions));
    }

    public bool HasAccounts => Accounts.Count > 0;
    public bool HasSessions => Sessions.Count > 0;
    public string AccountHeading { get; init; } = "Аккаунты и серверы";
    public string AddAccountLabel { get; init; } = "Добавить аккаунт или сервер";
    public bool SessionOnly { get; init; }
    public bool ShowRotation { get; init; }
    public bool CanScanQr { get; init; }
    public bool IsAdmin { get; init; }
    public bool AllowRotation
    {
        get => allowRotation;
        set
        {
            if (Set(ref allowRotation, value))
                RotationChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler? RotationChanged;
    public ObservableCollection<AccountSettingsItem> Accounts { get; } = [];
    public ObservableCollection<SessionSettingsItem> Sessions { get; } = [];
    public AsyncCommand PluginsCommand { get; init; } = new(() => Task.CompletedTask);
    public AsyncCommand AddAccountCommand { get; init; } = new(() => Task.CompletedTask);
    public AsyncCommand ImportThemeCommand { get; init; } = new(() => Task.CompletedTask);
    public AsyncCommand ImportQrCommand { get; init; } = new(() => Task.CompletedTask);
    public AsyncCommand ScanQrCommand { get; init; } = new(() => Task.CompletedTask);
    public AsyncCommand ExportThemeCommand { get; init; } = new(() => Task.CompletedTask);
    public AsyncCommand ExportQrCommand { get; init; } = new(() => Task.CompletedTask);
    public AsyncCommand EditThemeCommand { get; init; } = new(() => Task.CompletedTask);
    public AsyncCommand AdminCommand { get; init; } = new(() => Task.CompletedTask);
}

public sealed record AccountSettingsItem(
    string Label,
    AsyncCommand SwitchCommand,
    AsyncCommand RemoveCommand
);

public sealed record SessionSettingsItem(
    string DeviceName,
    string Activity,
    AsyncCommand RevokeCommand
);
