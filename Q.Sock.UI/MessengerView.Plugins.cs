using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Q.Sock.Core.Plugins;
using Q.Sock.UI.Plugins;
using static Q.Sock.UI.UiFactory;

namespace Q.Sock.UI;

public sealed partial class MessengerView
{
    private readonly Dictionary<string, LoadedClientPlugin> externalPlugins = [];
    private readonly HashSet<string> skippedPlugins = [];
    private bool refreshingPlugins;
    private bool showingLogin = true;
    private Control? customWorkspace;
    private Action? dialogClosed;
    private string? pluginError;
    private readonly SemaphoreSlim pluginUpdates = new(1, 1);
    private CancellationTokenSource pluginCancellation = new();

    private async Task UnloadClientPlugins()
    {
        pluginCancellation.Cancel();
        CloseDialog();
        await pluginUpdates.WaitAsync();
        try
        {
            if (customWorkspace is not null)
                root.Children.Remove(customWorkspace);
            customWorkspace = null;
            foreach (var plugin in externalPlugins.Values)
                await plugin.DisposeAsync();
            externalPlugins.Clear();
            skippedPlugins.Clear();
            clientPlugins.Clear();
            foreach (var entry in builtInPlugins)
                clientPlugins[entry.Key] = entry.Value;
            pluginCancellation.Dispose();
            pluginCancellation = new();
        }
        finally
        {
            pluginUpdates.Release();
        }
    }

    private static string PackageIdentity(PluginPackage package) =>
        Convert.ToBase64String(package.SigningPayload()) + ":" + package.PublisherFingerprint();

    private async Task RefreshClientPlugins(bool refreshNavigation = true)
    {
        if (client is null || refreshingPlugins)
            return;
        refreshingPlugins = true;
        await pluginUpdates.WaitAsync();
        var transport = client;
        var cancellation = pluginCancellation.Token;
        var added = new List<LoadedClientPlugin>();
        try
        {
            if (transport is null)
                return;
            cancellation.ThrowIfCancellationRequested();
            var target =
                OperatingSystem.IsBrowser() ? "web"
                : OperatingSystem.IsAndroid() ? "android"
                : "avalonia";
            manifest = await transport.PublicGet<Q.Sock.Shared.Models.UiManifest>("api/ui/manifest");
            PluginPackage[] packages;
            try
            {
                packages = await transport.Get<PluginPackage[]>(
                    $"api/ui/plugins?target={target}",
                    cancellation
                );
            }
            catch (HttpRequestException exception)
                when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                packages = [];
            }
            if (packages.Select(package => package.Id).Distinct().Count() != packages.Length)
                throw new InvalidDataException(
                    "Сервер вернул повторяющиеся идентификаторы модулей."
                );
            var next = new Dictionary<string, LoadedClientPlugin>();
            var installer = new ClientPluginInstaller(pluginCache, pluginTrust, pluginSignatures);
            foreach (var package in packages)
            {
                package.ValidateMetadata();
                if (package.Target != target)
                    throw new InvalidDataException("Модуль предназначен для другой платформы.");
                externalPlugins.TryGetValue(package.Id, out var current);
                var identity = PackageIdentity(package);
                if (current is not null && PackageIdentity(current.Package) == identity)
                {
                    next.Add(package.Id, current);
                    continue;
                }
                if (skippedPlugins.Contains(identity))
                {
                    if (current is not null)
                        next.Add(package.Id, current);
                    continue;
                }
                var installed = await installer.Install(
                    transport,
                    package,
                    ConfirmClientPlugin,
                    cancellation
                );
                if (installed is null)
                {
                    skippedPlugins.Add(identity);
                    if (current is not null)
                        next.Add(package.Id, current);
                    continue;
                }
                added.Add(installed);
                next.Add(package.Id, installed);
            }
            var replaced = externalPlugins
                .Values.Where(previous => !next.Values.Contains(previous))
                .ToArray();
            externalPlugins.Clear();
            foreach (var entry in next)
                externalPlugins.Add(entry.Key, entry.Value);
            clientPlugins.Clear();
            foreach (var entry in builtInPlugins)
                clientPlugins[entry.Key] = entry.Value;
            foreach (var entry in next)
                clientPlugins[entry.Key] = entry.Value.Instance;
            added.Clear();
            if (customWorkspace is not null && replaced.Length > 0)
            {
                root.Children.Remove(customWorkspace);
                customWorkspace = null;
            }
            foreach (var previous in replaced)
                await previous.DisposeAsync();
            await pluginCache.Prune(
                PluginCacheKeys.Server(transport.Server),
                next.Values.Select(plugin => PluginCacheKeys.Package(plugin.Package)).ToArray()
            );
            pluginError = null;
            if (refreshNavigation && !showingLogin)
            {
                await RefreshWorkspace();
                ApplyPluginShell();
            }
        }
        catch (Exception exception)
        {
            foreach (var plugin in added)
                await plugin.DisposeAsync();
            if (!cancellation.IsCancellationRequested)
            {
                pluginError = exception.Message;
                status.Text = "Не удалось обновить модули: " + exception.Message;
            }
        }
        finally
        {
            refreshingPlugins = false;
            pluginUpdates.Release();
        }
    }

    private async Task<bool> ConfirmClientPlugin(PluginPackage package)
    {
        var result = new TaskCompletionSource<bool>();
        await ShowDialog(
            "Разрешить модуль интерфейса?",
            Column(
                Text(package.Title, 20),
                Text($"Сервер: {client!.Server.Host} · Версия: {package.Version}"),
                Text(
                    "Модуль выполняет код с правами Q.Sock и может обращаться к файлам, сети и данным клиента."
                ),
                Text("Отпечаток ключа издателя", 12),
                new TextBox
                {
                    Text = package.PublisherFingerprint(),
                    IsReadOnly = true,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                Button(
                    "Доверять издателю и загрузить",
                    () =>
                    {
                        result.TrySetResult(true);
                        CloseDialog();
                        return Task.CompletedTask;
                    }
                ),
                Button(
                    "Пропустить",
                    () =>
                    {
                        result.TrySetResult(false);
                        CloseDialog();
                        return Task.CompletedTask;
                    }
                )
            )
        );
        dialogClosed = () => result.TrySetResult(false);
        return await result.Task;
    }

    private ClientPluginContext CreatePluginContext() =>
        new()
        {
            Client = client!,
            ChatId = selectedChat?.Id,
            RefreshWorkspace = () =>
                Dispatcher.UIThread.Post(async () => await Run(RefreshWorkspace)),
            ShowMessage = message => Dispatcher.UIThread.Post(() => status.Text = message),
        };

    private async Task OpenClientPlugin(LoadedClientPlugin plugin)
    {
        var view = plugin.Instance.CreateView(CreatePluginContext());
        if (plugin.Package.Slot != "workspace")
        {
            await ShowDialog(plugin.Package.Title, view);
            return;
        }
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(
            Row(
                Button(
                    "Вернуться к чатам",
                    () =>
                    {
                        root.Children.Remove(customWorkspace!);
                        customWorkspace = null;
                        workspace.IsVisible = true;
                        return Task.CompletedTask;
                    }
                ),
                Button("Настройки", () => Run(ShowSettings))
            )
        );
        Grid.SetRow(view, 1);
        layout.Children.Add(view);
        customWorkspace = layout;
        workspace.IsVisible = false;
        root.Children.Add(layout);
    }

    private Task ShowClientPlugins()
    {
        var list = Column(
            Text(
                "Модули устанавливает владелец сервера. Доверие к издателю сохраняется только для этого сервера.",
                12
            )
        );
        if (pluginError is not null)
            list.Children.Add(Text(pluginError));
        foreach (var plugin in externalPlugins.Values)
            list.Children.Add(
                Card(
                    Column(
                        Text(plugin.Package.Title, 18),
                        Text(
                            $"{plugin.Package.Id} · {plugin.Package.Version} · {plugin.Package.Target}"
                        ),
                        Text(plugin.Package.PublisherFingerprint(), 11)
                    )
                )
            );
        if (externalPlugins.Count == 0)
            list.Children.Add(Text("Загруженных модулей пока нет."));
        list.Children.Add(
            Button(
                "Проверить обновления",
                () =>
                    Run(async () =>
                    {
                        if (activeCall is not null || customWorkspace is not null)
                            throw new InvalidOperationException(
                                "Для обновления вернитесь к чатам и завершите звонок."
                            );
                        CloseDialog();
                        skippedPlugins.Clear();
                        await RefreshClientPlugins();
                        await ShowClientPlugins();
                    })
            )
        );
        return ShowDialog("Модули интерфейса", list);
    }
}
