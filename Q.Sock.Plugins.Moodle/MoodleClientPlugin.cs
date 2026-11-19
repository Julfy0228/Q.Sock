using Avalonia.Controls;
using Q.Sock.Shared.Models;
using Q.Sock.UI;
using Q.Sock.UI.Plugins;
using static Q.Sock.UI.UiFactory;

namespace Q.Sock.Plugins.Moodle;

public sealed class MoodleClientPlugin : IClientPlugin, IClientUiExtension
{
    public string Id => "moodle";

    public Control CreateView(ClientPluginContext context) => new LessonPlanningView(context);

    public IReadOnlyList<ClientUiAction> GetActions(ClientPluginContext context)
    {
        if (!context.ServerModuleIds.Contains("moodle"))
            return [];
        var actions = new List<ClientUiAction>
        {
            new()
            {
                Id = "moodle.courses",
                Title = "Учебные курсы",
                Icon = "SchoolOutlined",
                Location = ClientUiLocation.Sidebar,
                Execute = () => ShowCourses(context),
            },
        };
        if (context.Chat?.Managed == true)
            actions.Add(
                new()
                {
                    Id = "moodle.lessons",
                    Title = "Занятия курса",
                    Icon = "EventOutlined",
                    Location = ClientUiLocation.Chat,
                    Execute = () => ShowLessons(context),
                }
            );
        if (context.Chat is { Managed: true, CanManage: true })
            actions.Add(PlanningAction(context, ClientUiLocation.Chat));
        if (context.Guild is { Managed: true, CanManage: true })
            actions.Add(PlanningAction(context, ClientUiLocation.Workspace));
        if (context.Profile?.IsAdmin == true)
            actions.Add(
                new()
                {
                    Id = "moodle.policy",
                    Title = "Права студентов",
                    Icon = "SchoolOutlined",
                    Location = ClientUiLocation.ServerSettings,
                    Execute = () => ShowPolicy(context),
                }
            );
        return actions;
    }

    public string? GetManagedDescription(ClientPluginContext context) =>
        context.Chat?.Managed == true || context.Guild?.Managed == true
            ? "Состав, учебные группы и служебные роли обновляются из Moodle."
            : null;

    public string? GetRoleCaption(string role) =>
        role switch
        {
            "teacher" => "Преподаватель",
            "student" => "Студент",
            _ => null,
        };

    private static ClientUiAction PlanningAction(
        ClientPluginContext context,
        ClientUiLocation location
    ) =>
        new()
        {
            Id = "moodle.plan",
            Title = "Запланировать занятие",
            Icon = "EventOutlined",
            Location = location,
            Execute = () =>
                context.OpenDialog("Запланировать занятие", new LessonPlanningView(context), 960),
        };

    private static async Task ShowPolicy(ClientPluginContext context)
    {
        var policy = await context.Client.Get<ServerPolicy>("api/admin/policy");
        var creation = new CheckBox
        {
            Content = "Студенты могут создавать свои пространства",
            IsChecked = policy.StudentsCanCreateGuilds,
        };
        var deletion = new CheckBox
        {
            Content = "Подтверждать удаление пространств студентов",
            IsChecked = policy.ConfirmStudentGuildDeletion,
        };
        var form = Column(
            creation,
            deletion,
            Primary(
                "Сохранить",
                async () =>
                {
                    policy.StudentsCanCreateGuilds = creation.IsChecked == true;
                    policy.ConfirmStudentGuildDeletion = deletion.IsChecked == true;
                    await context.Client.Mutate("api/admin/policy", HttpMethod.Put, policy);
                    context.ShowMessage("Настройки Moodle сохранены");
                }
            )
        );
        await context.OpenDialog("Права студентов", form, 640);
    }

    private static async Task ShowCourses(ClientPluginContext context)
    {
        var courses = await context.Client.Get<MoodleCourse[]>("api/modules/moodle/courses");
        var list = new StackPanel { Spacing = 16 };
        foreach (var course in courses)
        {
            var current = course;
            list.Children.Add(
                Card(
                    Column(
                        Text(course.Name, 18, strong: true),
                        Text("Учебное пространство Moodle", 12, "muted"),
                        Primary("Открыть обсуждение", () => context.OpenChat(current.ChatId)),
                        Button("Открыть комнату", () => context.OpenChat(current.RoomId))
                    )
                )
            );
        }
        if (courses.Length == 0)
            list.Children.Add(Text("Нет доступных учебных курсов.", 14, "muted"));
        await context.OpenDialog("Учебные курсы", list, 640);
    }

    private static async Task ShowLessons(ClientPluginContext context)
    {
        var calls = await context.Client.Get<CallInfo[]>("api/calls");
        var list = new StackPanel { Spacing = 16 };
        foreach (
            var call in calls.Where(call =>
                context.Chats.Any(chat =>
                    chat.Id == call.ChatId && chat.GuildId == context.Chat?.GuildId
                )
            )
        )
        {
            var current = call;
            list.Children.Add(
                Card(
                    Column(
                        Text(call.Title, 18, strong: true),
                        Text(
                            call.Status == "live"
                                ? "Сейчас идёт"
                                : $"{call.StartsAtUtc?.ToLocalTime():dd.MM.yyyy HH:mm} · {call.DurationMinutes} мин",
                            13,
                            "muted"
                        ),
                        Primary("Перейти на занятие", () => context.OpenCall(current.Id))
                    )
                )
            );
        }
        if (list.Children.Count == 0)
            list.Children.Add(Text("Занятий пока нет.", 14, "muted"));
        await context.OpenDialog("Занятия курса", list, 640);
    }
}
