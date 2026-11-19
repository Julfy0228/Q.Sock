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
        var source = client!;
        foreach (var member in members)
        {
            var id = member.GetProperty("userId").GetGuid();
            var role = member.GetProperty("role").GetString() ?? "member";
            var name = member.GetProperty("displayName").GetString();
            if (string.IsNullOrWhiteSpace(name))
                name = member.GetProperty("userName").GetString() ?? "Участник";
            var actions = new ContextMenu();
            if (id != profile?.Id)
                AddMemberAction(actions, "Написать", () => WriteToMember(source, id));
            var canManage = !guild.Managed && (owner || profile?.IsAdmin == true)
                && role != "owner" && id != profile?.Id;
            Func<string, Task>? changeRole = null;
            if (canManage)
            {
                AddMemberAction(actions, "Исключить из пространства", async () =>
                {
                    await source.Mutate($"api/guilds/{guild.Id}/members/{id}", HttpMethod.Delete);
                    if (client == source && selectedGuild == guild.Id)
                        await ShowSpace();
                }, destructive: true);
                if (role is "member" or "moderator")
                    changeRole = async value =>
                    {
                        await source.Mutate(
                            $"api/guilds/{guild.Id}/members/{id}",
                            HttpMethod.Put,
                            new MemberUpdate { Role = value }
                        );
                        if (client == source && selectedGuild == guild.Id)
                            await ShowSpace();
                    };
            }
            list.Children.Add(MemberRow(name, id, role, [], actions, changeRole));
        }
        if (!guild.Managed && (owner || profile?.IsAdmin == true))
            list.Children.Add(
                Danger(
                    "Удалить пространство…",
                    () =>
                        ShowDialog(
                            "Удаление пространства",
                            Column(
                                Text(
                                    $"Удалить пространство «{guild.Name}» вместе с его каналами и перепиской?"
                                ),
                                Danger(
                                    "Удалить",
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
