using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Q.Sock.Shared.Models;
using Q.Sock.UI;
using Q.Sock.UI.Plugins;
using static Q.Sock.UI.UiFactory;

namespace Q.Sock.Plugins.Moodle;

public sealed class LessonPlanningView : UserControl
{
    private readonly ClientPluginContext context;
    private readonly ComboBox course = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox title = Input("Название занятия");
    private readonly CalendarDatePicker date = new()
    {
        SelectedDate = DateTime.Today.AddDays(1),
        DisplayDateStart = DateTime.Today,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    private readonly TimePicker time = new()
    {
        SelectedTime = new TimeSpan(10, 0, 0),
        ClockIdentifier = "24HourClock",
    };
    private readonly TextBox email = Input("Почта для связи (необязательно)");
    private readonly Slider duration = new()
    {
        Minimum = 15,
        Maximum = 240,
        Value = 90,
        TickFrequency = 15,
        IsSnapToTickEnabled = true,
    };
    private readonly ToggleSwitch reminder = new()
    {
        Content = "Напомнить участникам",
        IsChecked = true,
        OnContent = "Включено",
        OffContent = "Выключено",
    };
    private readonly TextBox materialUrl = Input("Ссылка на учебный материал");
    private readonly TextBlock result = Text("");
    private readonly TextBlock selectedMaterial = Text("Материалов пока нет");
    private readonly List<string> materials = [];
    private readonly Button save;
    private readonly TextBox courseSearch = Input("Найти курс");
    private MoodleCourse[] courses = [];
    private readonly Grid sections = new() { ColumnSpacing = 24, RowSpacing = 24 };
    private readonly StackPanel materialSection = new() { Spacing = 16 };
    private readonly TextBlock materialPage = Text("Материалов пока нет", 11, "muted");
    private readonly TextBlock materialName = Text("Материалы занятия", 16, strong: true);
    private int materialIndex;

    public LessonPlanningView(ClientPluginContext context)
    {
        this.context = context;
        var durationLabel = Text("Продолжительность: 90 минут");
        duration.PropertyChanged += (_, change) =>
        {
            if (change.Property == Slider.ValueProperty)
                durationLabel.Text = $"Продолжительность: {(int)duration.Value} минут";
        };
        save = Primary("Сохранить занятие", Save);
        save.HorizontalAlignment = HorizontalAlignment.Center;
        save.IsEnabled = false;
        time.HorizontalAlignment = HorizontalAlignment.Stretch;
        var when = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,112"),
            ColumnSpacing = 12,
        };
        date.MinWidth = 0;
        time.MinWidth = 260;
        when.Children.Add(date);
        var timeButton = Button("10:00", () => Task.CompletedTask);
        timeButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        timeButton.MinHeight = 56;
        var timeFlyout = new Flyout
        {
            Content = Column(Text("Начало занятия", 16, strong: true), time),
        };
        timeButton.Click += (_, _) => timeFlyout.ShowAt(timeButton);
        time.PropertyChanged += (_, change) =>
        {
            if (change.Property == TimePicker.SelectedTimeProperty)
                timeButton.Content = time.SelectedTime?.ToString(@"hh\:mm") ?? "Время";
        };
        Grid.SetColumn(timeButton, 1);
        when.Children.Add(timeButton);
        var parameters = Column(
            Text("Основное", 18, strong: true),
            Field("Название занятия", title),
            courseSearch,
            Field("Курс Moodle", course),
            Text("Дата и время", 12, "secondary"),
            when,
            Field("Почта для связи", email),
            Text("Необязательно. Видна участникам занятия.", 11, "muted"),
            reminder,
            durationLabel,
            duration,
            Text("15 мин                       120 мин                       240 мин", 11, "muted")
        );
        parameters.Spacing = 16;
        var materialControls = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*,40") };
        var previous = IconButton(
            "ChevronLeft",
            "Предыдущий материал",
            () => SelectMaterial(-1),
            compact: true
        );
        var next = IconButton(
            "ChevronRight",
            "Следующий материал",
            () => SelectMaterial(1),
            compact: true
        );
        materialControls.Children.Add(previous);
        var page = materialPage;
        page.HorizontalAlignment = HorizontalAlignment.Center;
        page.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(page, 1);
        materialControls.Children.Add(page);
        Grid.SetColumn(next, 2);
        materialControls.Children.Add(next);
        materialSection.Children.Add(Text("Материалы занятия", 18, strong: true));
        materialSection.Children.Add(
            Text("Изображения и ссылки на материалы курса", 12, "secondary")
        );
        materialSection.Children.Add(
            Card(
                Column(
                    new MaterialIcon("ArticleOutlined", 48)
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 16),
                    },
                    materialName,
                    selectedMaterial,
                    materialControls,
                    Button(
                        "Открыть материал",
                        async () =>
                        {
                            if (materials.Count > 0 && TopLevel.GetTopLevel(this) is { } top)
                                await top.Launcher.LaunchUriAsync(
                                    new Uri(materials[materialIndex])
                                );
                        }
                    )
                )
            )
        );
        materialSection.Children.Add(Field("Ссылка на материал", materialUrl));
        materialSection.Children.Add(IconButton("Add", "Добавить материал", AddMaterial));
        materialSection.Children.Add(
            IconButton("DeleteOutlineOutlined", "Убрать выбранный материал", RemoveMaterial)
        );
        sections.Children.Add(parameters);
        sections.Children.Add(materialSection);
        var footer = Column(save, Text("Изменения появятся в курсе и чате", 11, "muted"), result);
        footer.Margin = new Thickness(0, 12, 0, 0);
        Content = Column(sections, footer);
        SizeChanged += (_, _) =>
        {
            var wide = Bounds.Width >= 700;
            sections.ColumnDefinitions = new ColumnDefinitions(wide ? "*,*" : "*");
            sections.RowDefinitions = new RowDefinitions(wide ? "Auto" : "Auto,Auto");
            Grid.SetColumn(materialSection, wide ? 1 : 0);
            Grid.SetRow(materialSection, wide ? 0 : 1);
        };
        courseSearch.TextChanged += (_, _) =>
        {
            var selected = course.SelectedItem;
            course.ItemsSource = courses
                .Where(item =>
                    item.Name.Contains(
                        courseSearch.Text ?? "",
                        StringComparison.CurrentCultureIgnoreCase
                    )
                )
                .ToArray();
            if (
                selected is MoodleCourse item
                && ((MoodleCourse[])course.ItemsSource).Contains(item)
            )
                course.SelectedItem = item;
        };
        AttachedToVisualTree += async (_, _) => await LoadCourses();
    }

    private async Task LoadCourses()
    {
        try
        {
            courses = await context.Client.Get<MoodleCourse[]>("api/modules/moodle/courses");
            course.ItemsSource = courses;
            course.SelectedItem =
                courses.FirstOrDefault(item =>
                    item.ChatId == context.ChatId || item.RoomId == context.ChatId
                ) ?? courses.FirstOrDefault();
            save.IsEnabled = courses.Length > 0;
            if (courses.Length == 0)
                result.Text = "Нет доступных учебных курсов.";
        }
        catch (Exception exception)
        {
            result.Text = exception.Message;
        }
    }

    private Task AddMaterial()
    {
        if (
            !Uri.TryCreate(materialUrl.Text?.Trim(), UriKind.Absolute, out var address)
            || address.Scheme is not ("https" or "http")
        )
        {
            result.Text = "Укажите ссылку HTTP или HTTPS.";
            return Task.CompletedTask;
        }
        if (materials.Count >= 20)
        {
            result.Text = "Можно добавить до 20 материалов.";
            return Task.CompletedTask;
        }
        materials.Add(address.AbsoluteUri);
        materialIndex = materials.Count - 1;
        materialUrl.Text = "";
        return SelectMaterial(0);
    }

    private Task SelectMaterial(int offset)
    {
        if (materials.Count == 0)
            selectedMaterial.Text =
                "Прикрепите ссылки на изображения, документы или страницы курса.";
        else
        {
            materialIndex = (materialIndex + offset + materials.Count) % materials.Count;
            selectedMaterial.Text =
                $"{materialIndex + 1} / {materials.Count}\n{materials[materialIndex]}";
        }
        materialPage.Text =
            materials.Count == 0
                ? "Материалов пока нет"
                : $"{materialIndex + 1} / {materials.Count}";
        materialName.Text =
            materials.Count == 0 ? "Добавьте материалы курса" : $"Материал {materialIndex + 1}";
        return Task.CompletedTask;
    }

    private Task RemoveMaterial()
    {
        if (materials.Count > 0)
        {
            materials.RemoveAt(materialIndex);
            materialIndex = Math.Max(0, materialIndex - 1);
        }
        return SelectMaterial(0);
    }

    private async Task Save()
    {
        if (
            course.SelectedItem is not MoodleCourse selected
            || date.SelectedDate is null
            || time.SelectedTime is null
        )
        {
            result.Text = "Выберите курс, дату и время.";
            return;
        }
        save.IsEnabled = false;
        try
        {
            var localDate = DateTime.SpecifyKind(
                date.SelectedDate.Value.Date + time.SelectedTime.Value,
                DateTimeKind.Local
            );
            await context.Client.Post<CallInfo>(
                "api/modules/moodle/lessons",
                new LessonRequest
                {
                    ChatId = selected.RoomId,
                    Title = title.Text?.Trim() ?? "",
                    StartsAtUtc = localDate.ToUniversalTime(),
                    DurationMinutes = (int)duration.Value,
                    ContactEmail = email.Text?.Trim(),
                    Reminder = reminder.IsChecked == true,
                    Materials = materials.ToArray(),
                }
            );
            result.Text =
                "Занятие сохранено. Публикация в календаре Moodle выполняется автоматически.";
            context.ShowMessage("Занятие запланировано");
            context.RefreshWorkspace();
        }
        catch (Exception exception)
        {
            result.Text = exception.Message;
        }
        finally
        {
            save.IsEnabled = true;
        }
    }
}

public sealed class MoodleCourse
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }
    public Guid RoomId { get; set; }
    public string Name { get; set; } = "";

    public override string ToString() => Name;
}
