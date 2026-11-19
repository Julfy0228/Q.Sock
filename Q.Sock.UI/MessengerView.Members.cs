using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Q.Sock.Core;
using Q.Sock.Shared.Models;
using static Q.Sock.UI.UiFactory;

namespace Q.Sock.UI;

public sealed partial class MessengerView
{
    private static string MemberName(MemberInfo member) =>
        string.IsNullOrWhiteSpace(member.DisplayName) ? member.UserName : member.DisplayName;

    private static Grid PersonIdentity(string name, Guid id)
    {
        var identity = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*"), ColumnSpacing = 8 };
        var avatar = Avatar(name, id, AvatarKind.User, 30);
        avatar.VerticalAlignment = VerticalAlignment.Center;
        identity.Children.Add(avatar);
        var label = Ellipsis(name, 14);
        label.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(label, name);
        Grid.SetColumn(label, 1);
        identity.Children.Add(label);
        return identity;
    }

    private static void AttachContextMenu(Control control, ContextMenu menu)
    {
        control.ContextMenu = menu;
        control.SetValue(InputElement.IsHoldingEnabledProperty, true);
        control.AddHandler(
            InputElement.HoldingEvent,
            (_, args) =>
            {
                if (args.HoldingState != HoldingState.Started || args.PointerType == PointerType.Mouse)
                    return;
                if (!menu.IsOpen)
                    menu.Open(control);
                args.Handled = true;
            }
        );
    }

    private void AddMemberAction(ContextMenu menu, string title, Func<Task> action, bool destructive = false)
    {
        var item = new MenuItem { Header = title };
        if (destructive)
            item.Classes.Add("danger");
        item.Click += async (_, _) => await Run(action);
        menu.Items.Add(item);
    }

    private Control MemberRow(
        string name, Guid id, string role, IReadOnlyList<string> groups, ContextMenu actions,
        Func<string, Task>? changeRole = null
    )
    {
        var identity = PersonIdentity(name, id);
        Control leading = identity;
        if (actions.Items.Count > 0)
        {
            var button = Button($"Действия: {name}", () => Task.CompletedTask);
            button.Classes.Add("quiet");
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Padding = new Thickness(2, 4);
            button.Content = identity;
            button.Click += (_, _) => actions.Open(button);
            AttachContextMenu(button, actions);
            leading = button;
        }

        const double roleWidth = 128;
        const double roleHeight = 40;
        Control roleControl;
        if (changeRole is null)
        {
            var label = Text(RoleLabel(role), 13, "secondary");
            label.TextAlignment = TextAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            roleControl = Surface(label, "surface", new Thickness(4, 0), 5);
        }
        else
        {
            var roles = new[] { new Choice("member", "Участник"), new Choice("moderator", "Модератор") };
            var original = roles.First(choice => choice.Value == role);
            var rolePicker = new ComboBox
            {
                ItemsSource = roles,
                SelectedItem = original,
                FontSize = 13,
                Padding = new Thickness(8, 0),
            };
            rolePicker.SelectionChanged += async (_, _) =>
            {
                if (!rolePicker.IsEnabled || rolePicker.SelectedItem is not Choice next || next == original)
                    return;
                rolePicker.IsEnabled = false;
                await Run(async () =>
                {
                    try
                    {
                        await changeRole(next.Value);
                    }
                    catch
                    {
                        rolePicker.SelectedItem = original;
                        throw;
                    }
                    finally
                    {
                        rolePicker.IsEnabled = true;
                    }
                });
            };
            roleControl = rolePicker;
        }
        roleControl.Width = roleWidth;
        roleControl.Height = roleHeight;
        roleControl.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        row.Children.Add(leading);
        Grid.SetColumn(roleControl, 1);
        row.Children.Add(roleControl);
        if (groups.Count == 0)
            return row;
        var details = Column(row, Text(string.Join(", ", groups), 12, "muted"));
        details.Spacing = 4;
        return details;
    }

    private async Task WriteToMember(MessengerClient source, Guid userId)
    {
        var direct = await source.Post<JsonElement>(
            "api/chats", new CreateChat { Type = "direct", UserIds = [userId] }
        );
        if (client != source)
            return;
        var id = direct.GetProperty("id").GetGuid();
        var updated = await source.Get<ChatInfo[]>("api/chats");
        if (client == source && updated.FirstOrDefault(chat => chat.Id == id) is { } chat)
        {
            chats = updated;
            CloseDialog();
            await SelectChat(chat);
        }
    }

    private bool CanManageChat(ChatInfo chat) => chat.CanManage || profile?.IsAdmin == true;

    private Task AddChatMember(MessengerClient source, ChatInfo chat) =>
        PickUser(async person =>
        {
            await source.Mutate($"api/chats/{chat.Id}/members/{person.Id}", HttpMethod.Put, new MemberUpdate());
            if (client == source && selectedChat?.Id == chat.Id)
            {
                CloseDialog();
                await LoadConversationMembers();
            }
        });

    private Control ChatMemberRow(MessengerClient source, ChatInfo chat, MemberInfo member)
    {
        var own = member.UserId == profile?.Id;
        var owner = conversationMembers.Any(person => person.UserId == profile?.Id && person.Role == "owner");
        var canChangeRole = owner || profile?.IsAdmin == true;
        var canModerate = CanManageChat(chat) && !own && member.Role != "owner"
            && (member.Role != "moderator" || canChangeRole);
        var actions = new ContextMenu();
        if (!own)
            AddMemberAction(actions, "Написать", () => WriteToMember(source, member.UserId));
        if (canModerate)
            AddMemberAction(actions, "Права участника", () => ShowMemberPermissions(source, chat, member));
        if (canModerate && !chat.Managed && chat.GuildId is null && (member.Role != "moderator" || owner))
            AddMemberAction(actions, "Исключить из чата", async () =>
            {
                await source.Mutate($"api/chats/{chat.Id}/members/{member.UserId}", HttpMethod.Delete);
                await ReloadMemberPanel(source, chat);
            }, destructive: true);
        Func<string, Task>? setRole = null;
        if (canModerate && canChangeRole && !chat.Managed && member.Role is "member" or "moderator")
            setRole = role => UpdateChatMember(source, chat, member.UserId,
                new MemberUpdate
                {
                    Role = role,
                    CanWrite = member.CanWrite,
                    CanSpeak = member.CanSpeak,
                    CanConnect = member.CanConnect,
                    CanShare = member.CanShare,
                });
        return MemberRow(MemberName(member), member.UserId, member.Role, member.Groups, actions, setRole);
    }

    private async Task UpdateChatMember(MessengerClient source, ChatInfo chat, Guid userId, MemberUpdate update)
    {
        await source.Mutate($"api/chats/{chat.Id}/members/{userId}", HttpMethod.Put, update);
        await ReloadMemberPanel(source, chat);
    }

    private async Task ReloadMemberPanel(MessengerClient source, ChatInfo chat)
    {
        if (client == source && selectedChat?.Id == chat.Id)
            await LoadConversationMembers();
    }

    private Task ShowMemberPermissions(MessengerClient source, ChatInfo chat, MemberInfo member)
    {
        var write = new CheckBox { Content = "Писать", IsChecked = member.CanWrite };
        var speak = new CheckBox { Content = "Говорить", IsChecked = member.CanSpeak };
        var connect = new CheckBox { Content = "Подключаться к звонкам", IsChecked = member.CanConnect };
        var share = new CheckBox { Content = "Показывать экран", IsChecked = member.CanShare };
        return ShowDialog(
            "Права участника",
            Column(
                PersonIdentity(MemberName(member), member.UserId),
                write, speak, connect, share,
                Primary("Сохранить права", () => Run(async () =>
                {
                    await UpdateChatMember(source, chat, member.UserId,
                        new MemberUpdate
                        {
                            Role = member.Role,
                            CanWrite = write.IsChecked == true,
                            CanSpeak = speak.IsChecked == true,
                            CanConnect = connect.IsChecked == true,
                            CanShare = share.IsChecked == true,
                        });
                    CloseDialog();
                }))
            )
        );
    }
}
