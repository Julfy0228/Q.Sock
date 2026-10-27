using Avalonia.Controls;
using Q.Sock.Core;
using Q.Sock.Shared.Models;

namespace Q.Sock.UI.Plugins;

public interface IClientPlugin
{
    string Id { get; }
    Control CreateView(ClientPluginContext context);
}

public sealed class ClientPluginContext
{
    public required MessengerClient Client { get; init; }
    public IReadOnlySet<string> ServerModuleIds { get; init; } = new HashSet<string>();
    public Guid? ChatId { get; init; }
    public ChatInfo? Chat { get; init; }
    public GuildInfo? Guild { get; init; }
    public Profile? Profile { get; init; }
    public IReadOnlyList<ChatInfo> Chats { get; init; } = [];
    public IReadOnlyList<GuildInfo> Guilds { get; init; } = [];
    public Func<string, Control, double, Task> OpenDialog { get; init; } =
        (_, _, _) => Task.CompletedTask;
    public Func<Guid, Task> OpenChat { get; init; } = _ => Task.CompletedTask;
    public Func<Guid, Task> OpenCall { get; init; } = _ => Task.CompletedTask;
    public required Action RefreshWorkspace { get; init; }
    public required Action<string> ShowMessage { get; init; }
}

public enum ClientUiLocation
{
    Sidebar,
    Workspace,
    Chat,
    ServerSettings,
}

public sealed class ClientUiAction
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Icon { get; init; } = "ExtensionOutlined";
    public ClientUiLocation Location { get; init; }
    public Func<Task> Execute { get; init; } = () => Task.CompletedTask;
}

public interface IClientUiExtension
{
    IReadOnlyList<ClientUiAction> GetActions(ClientPluginContext context);
    string? GetManagedDescription(ClientPluginContext context) => null;
    string? GetRoleCaption(string role) => null;
    Control? CreateShell(ClientPluginContext context) => null;
}
