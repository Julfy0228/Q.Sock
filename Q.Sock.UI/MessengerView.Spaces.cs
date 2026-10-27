using System.Net.Http;
using System.Text.Json;
using Avalonia.Controls;
using Q.Sock.Shared.Models;
using static Q.Sock.UI.UiFactory;

namespace Q.Sock.UI;

public sealed partial class MessengerView
{
    private string RoleLabel(string role) => ExtensionRoleCaption(role);

    private async Task PickUser(Func<Profile, Task> select)
    {
        var query = Input("Имя или логин");
        var results = new StackPanel { Spacing = 10 };
        await ShowDialog(
            "Выбор пользователя",
            Column(
                query,
                Button(
                    "Найти",
                    () =>
                        Run(async () =>
                        {
                            var people = await client!.Get<Profile[]>(
                                $"api/users?search={Uri.EscapeDataString(query.Text?.TrimStart('@') ?? "")}"
                            );
                            results.Children.Clear();
                            foreach (var person in people.Where(person => person.Id != profile?.Id))
                                results.Children.Add(
                                    Button(
                                        string.IsNullOrWhiteSpace(person.DisplayName)
                                            ? person.UserName
                                            : person.DisplayName,
                                        () => Run(() => select(person))
                                    )
                                );
                            if (results.Children.Count == 0)
                                results.Children.Add(Text("Пользователи не найдены"));
                        })
                ),
                results
            )
        );
    }

    private async Task ShowSpace()
    {
        var guild = guilds.FirstOrDefault(item => item.Id == selectedGuild);
        if (guild is null)
            return;
        var members = await client!.Get<JsonElement[]>($"api/guilds/{guild.Id}/members");
        var list = Column(Text(guild.Name, 22));
        if (guild.Managed)
            list.Children.Add(Text("Название, состав и роли управляются интеграцией сервера."));
        if (guild.CanManage && !guild.Managed)
        {
            list.Children.Add(
                Button(
                    "Переименовать",
                    () =>
                        ShowTextForm(
                            "Название пространства",
                            "Название",
                            guild.Name,
                            async value =>
                            {
                                await client.Mutate(
                                    $"api/guilds/{guild.Id}",
                                    HttpMethod.Patch,
                                    new NamedRequest { Name = value }
                                );
                                await RefreshWorkspace();
                            }
                        )
                )
            );
            list.Children.Add(
                Button(
                    "Добавить участника",
                    () =>
                        Run(() =>
                            PickUser(async person =>
                            {
                                await client.Mutate(
                                    $"api/guilds/{guild.Id}/members/{person.Id}",
                                    HttpMethod.Put,
                                    new MemberUpdate()
                                );
                                await ShowSpace();
                            })
                        )
                )
            );
        }
        var ownRole = members.FirstOrDefault(member =>
            member.GetProperty("userId").GetGuid() == profile?.Id
        );
        var owner =
            ownRole.ValueKind != JsonValueKind.Undefined
            && ownRole.GetProperty("role").GetString() == "owner";
        foreach (var member in members)
        {
            var id = member.GetProperty("userId").GetGuid();
            var role = member.GetProperty("role").GetString() ?? "member";
            var name = member.GetProperty("displayName").GetString();
            var item = Column(
                Text(
                    string.IsNullOrWhiteSpace(name)
                        ? member.GetProperty("userName").GetString()!
                        : name
                ),
                Text(RoleLabel(role), 12)
            );
            if (id != profile?.Id)
                item.Children.Add(
                    Button(
                        "Написать",
                        () =>
                            Run(async () =>
                            {
                                var direct = await client.Post<JsonElement>(
                                    "api/chats",
                                    new CreateChat { Type = "direct", UserIds = [id] }
                                );
                                selectedGuild = null;
                                await RefreshWorkspace();
                                await SelectChat(
                                    chats.Single(chat =>
                                        chat.Id == direct.GetProperty("id").GetGuid()
                                    )
                                );
                                CloseDialog();
                            })
                    )
                );
            if (
                !guild.Managed
                && (owner || profile?.IsAdmin == true)
                && role != "owner"
                && id != profile?.Id
            )
            {
                var roles = new[]
                {
                    new Choice("member", "Участник"),
                    new Choice("moderator", "Модератор"),
                };
                var selection = new ComboBox
                {
                    ItemsSource = roles,
                    SelectedItem = roles.First(choice => choice.Value == role),
                };
                item.Children.Add(selection);
                item.Children.Add(
                    Button(
                        "Сохранить роль",
                        () =>
                            Run(async () =>
                            {
                                await client.Mutate(
                                    $"api/guilds/{guild.Id}/members/{id}",
                                    HttpMethod.Put,
                                    new MemberUpdate
                                    {
                                        Role = ((Choice)selection.SelectedItem!).Value,
                                    }
                                );
                                await ShowSpace();
                            })
                    )
                );
                item.Children.Add(
                    Button(
                        "Исключить",
                        () =>
                            Run(async () =>
                            {
                                await client.Mutate(
                                    $"api/guilds/{guild.Id}/members/{id}",
                                    HttpMethod.Delete
                                );
                                await ShowSpace();
                            })
                    )
                );
            }
            list.Children.Add(Card(item));
        }
        if (!guild.Managed && (owner || profile?.IsAdmin == true))
            list.Children.Add(
                Button(
                    "Удалить пространство…",
                    () =>
                        ShowDialog(
                            "Удаление пространства",
                            Column(
                                Text(
                                    $"Удалить пространство «{guild.Name}» вместе с его каналами и перепиской?"
                                ),
                                Button(
                                    "Подтвердить",
                                    () =>
                                        Run(async () =>
                                        {
                                            var result = await client.Delete<JsonElement>(
                                                $"api/guilds/{guild.Id}"
                                            );
                                            status.Text = result.GetProperty("pending").GetBoolean()
                                                ? "Заявка передана администратору"
                                                : "Пространство удалено";
                                            selectedGuild = null;
                                            CloseDialog();
                                            await RefreshWorkspace();
                                        })
                                )
                            )
                        )
                )
            );
        await ShowDialog("Пространство", list);
    }
}
