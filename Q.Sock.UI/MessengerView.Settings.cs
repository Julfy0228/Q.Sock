using System.Net.Http;
using Q.Sock.Core;
using Q.Sock.Shared.Models.Auth;
using Q.Sock.UI.ViewModels;
using Q.Sock.UI.Views;

namespace Q.Sock.UI;

public sealed partial class MessengerView
{
    private async Task ShowSettings()
    {
        var model = new SettingsViewModel
        {
            AccountHeading = browserOrigin is null ? "Аккаунты и серверы" : "Аккаунты",
            AddAccountLabel = browserOrigin is null
                ? "Добавить аккаунт или сервер"
                : "Добавить аккаунт",
            SessionOnly = accountRepository is IAccountPersistenceStatus { SessionOnly: true },
            ShowRotation = displaySettings is not null,
            AllowRotation = displaySettings?.AllowRotation == true,
            CanScanQr = qrScanner is not null,
            IsAdmin = profile?.IsAdmin == true,
            PluginsCommand = new(() => Run(ShowClientPlugins)),
            AddAccountCommand = new(() =>
            {
                CloseDialog();
                ShowLogin();
                return Task.CompletedTask;
            }),
            ImportThemeCommand = new(() => Run(ImportTheme)),
            ImportQrCommand = new(() => Run(ImportQrTheme)),
            ScanQrCommand = new(() => Run(ScanTheme)),
            ExportThemeCommand = new(() => Run(() => ExportTheme(false))),
            ExportQrCommand = new(() => Run(() => ExportTheme(true))),
            EditThemeCommand = new(() => Run(ShowThemeEditor)),
            AdminCommand = new(() => Run(ShowAdmin)),
        };
        model.RotationChanged += async (_, _) =>
        {
            if (displaySettings is not null)
                await Run(() => displaySettings.SetAllowRotation(model.AllowRotation));
        };
        foreach (var saved in accounts.ToArray())
        {
            model.Accounts.Add(
                new(
                    saved.ToString(),
                    new(() =>
                        Run(async () =>
                        {
                            await ConnectAccount(saved);
                            CloseDialog();
                        })
                    ),
                    new(() =>
                        Run(async () =>
                        {
                            accounts.Remove(saved);
                            await accountRepository.SaveAsync(accounts);
                            await ShowSettings();
                        })
                    )
                )
            );
        }
        foreach (var session in await client!.Get<DeviceSessionResponse[]>("api/sessions"))
        {
            model.Sessions.Add(
                new(
                    session.DeviceName,
                    session.IsCurrent
                        ? "Это устройство"
                        : $"Последняя активность: {session.LastActivityAtUtc.ToLocalTime():g}",
                    new(() => Run(() => RevokeSession(session)))
                )
            );
        }
        await ShowDialog("Настройки", new SettingsView { DataContext = model });
    }

    private async Task RevokeSession(DeviceSessionResponse session)
    {
        await client!.Mutate($"api/sessions/{session.Id}", HttpMethod.Delete);
        if (session.IsCurrent)
        {
            accounts.Remove(account!);
            await accountRepository.SaveAsync(accounts);
            ShowLogin();
        }
        else
            await ShowSettings();
    }
}
