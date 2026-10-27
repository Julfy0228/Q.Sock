using Avalonia.Controls;
using Q.Sock.UI.Plugins;

namespace Q.Sock.UI;

public sealed partial class MessengerView
{
    private ClientPluginContext PluginContext() =>
        new()
        {
            Client = client!,
            ServerModuleIds = manifest?.Modules.Select(module => module.Id).ToHashSet() ?? [],
            ChatId = selectedChat?.Id,
            Chat = selectedChat,
            Guild = guilds.FirstOrDefault(guild => guild.Id == selectedGuild),
            Profile = profile,
            Chats = chats,
            Guilds = guilds,
            RefreshWorkspace = () =>
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                    await Run(RefreshWorkspace)
                ),
            ShowMessage = message =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => status.Text = message),
            OpenDialog = (title, view, width) => ShowDialog(title, view, width),
            OpenChat = async id =>
            {
                CloseDialog();
                await SelectChat(
                    chats.FirstOrDefault(chat => chat.Id == id)
                        ?? throw new InvalidOperationException("Чат недоступен.")
                );
            },
            OpenCall = async id =>
            {
                CloseDialog();
                await OpenCall(id);
            },
        };

    private IReadOnlyList<ClientUiAction> PluginActions(ClientUiLocation location)
    {
        if (client is null)
            return [];
        var result = new List<ClientUiAction>();
        foreach (var extension in clientPlugins.Values.OfType<IClientUiExtension>())
        {
            try
            {
                result.AddRange(
                    extension
                        .GetActions(PluginContext())
                        .Where(action => action.Location == location)
                );
            }
            catch (Exception exception)
            {
                pluginError = exception.Message;
            }
        }
        return result;
    }

    private string ManagedDescription()
    {
        foreach (var extension in clientPlugins.Values.OfType<IClientUiExtension>())
        {
            var description = extension.GetManagedDescription(PluginContext());
            if (!string.IsNullOrWhiteSpace(description))
                return description;
        }
        return "Состав и права управляются интеграцией сервера.";
    }

    private string ExtensionRoleCaption(string role)
    {
        foreach (var extension in clientPlugins.Values.OfType<IClientUiExtension>())
            if (extension.GetRoleCaption(role) is { } caption)
                return caption;
        return role switch
        {
            "owner" => "Владелец",
            "moderator" => "Модератор",
            "member" => "Участник",
            _ => role,
        };
    }

    private void ApplyPluginShell()
    {
        if (customWorkspace is not null)
            return;
        foreach (var extension in clientPlugins.Values.OfType<IClientUiExtension>())
        {
            if (extension.CreateShell(PluginContext()) is not { } view)
                continue;
            customWorkspace = view;
            root.Children.Add(view);
            return;
        }
    }
}
