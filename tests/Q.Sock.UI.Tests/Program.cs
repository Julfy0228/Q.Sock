using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Q.Sock.Core;
using Q.Sock.Plugins.Moodle;
using Q.Sock.UI;
using Q.Sock.UI.Plugins;
using Q.Sock.UI.ViewModels;
using Q.Sock.UI.Views;

AppBuilder
    .Configure<Application>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia()
    .SetupWithoutStarting();

Application.Current!.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

XamlViewChecks.Run();
DocumentationUiChecks.Run();

Check(MaterialIcon.Names.Count == 10750, "Каталог MUI обрезан.");
foreach (var name in MaterialIcon.Names)
{
    try
    {
        _ = new MaterialIcon(name);
    }
    catch (Exception exception)
    {
        throw new InvalidOperationException($"Не удалось прочитать геометрию {name}.", exception);
    }
}

Check(
    AppIcons.Create("plus") is MaterialIcon { IconName: "Add" },
    "Основным набором должен быть Material."
);
Check(
    AppIcons.Create("message-circle") is MaterialIcon { IconName: "ChatBubble" },
    "Старые сообщения должны поддерживать прежние имена."
);
Check(
    AppIcons.Create("chat_bubble_outlined") is MaterialIcon { IconName: "ChatBubbleOutlined" },
    "Имена в нижнем регистре с подчёркиванием не распознаны."
);
Check(
    AppIcons.Create("material/Call") is MaterialIcon { IconName: "Call" },
    "Явное имя Material не распознано."
);
Check(!AppIcons.Exists("material/ThisIconDoesNotExist"), "Неизвестное имя не должно приниматься.");

var originalMessage = ":ChatBubble: :smile: :material/Call: `:Call:`";
var markdown = new MarkdownView(originalMessage);
var paragraph = (SelectableTextBlock)markdown.Children.Single();
var inlines = paragraph.Inlines!.ToArray();
Check(
    !inlines.OfType<InlineUIContainer>().Any(),
    "Материальные иконки и смайлики не должны подставляться в сообщения."
);
Check(
    string.Concat(inlines.OfType<Run>().Select(run => run.Text))
        == originalMessage.Replace("`", ""),
    "Текстовые обозначения должны сохраняться обычным текстом."
);

var circle = Render("Circle");
Check(Alpha(circle, 48, 48) == 255, "Заливка Material не должна превращаться в обводку.");
var ring = Render("RadioButtonUnchecked");
Check(Alpha(ring, 48, 48) == 0, "Отверстие контура должно оставаться прозрачным.");
var twoTone = Render("AccountCircleTwoTone");
Check(Alpha(twoTone, 48, 20) is >= 74 and <= 79, "Прозрачность Two Tone потеряна.");
var transformed = Render("PlayCircleFilledWhite");
Check(
    Alpha(transformed, 48, 12) == 255 && Alpha(transformed, 48, 2) == 0,
    "SVG transform должен сохранять масштаб иконки."
);
var grouped = Render("MedicationLiquidTwoTone");
Check(
    Alpha(grouped, 18, 34) is >= 74 and <= 79,
    "Прозрачность группы должна применяться после наложения её элементов."
);

var output = args.FirstOrDefault();
if (output is not null)
{
    Directory.CreateDirectory(output);
    var gallery = new WrapPanel
    {
        Width = 576,
        ItemWidth = 144,
        ItemHeight = 140,
    };
    foreach (
        var name in new[]
        {
            "Chat",
            "ChatOutlined",
            "ChatRounded",
            "ChatSharp",
            "ChatTwoTone",
            "AccountCircleTwoTone",
            "MedicationLiquidTwoTone",
            "PlayCircleFilledWhite",
        }
    )
    {
        gallery.Children.Add(
            new StackPanel
            {
                Margin = new Thickness(10),
                Children =
                {
                    new MaterialIcon(name, 72) { Foreground = Brushes.DodgerBlue },
                    new TextBlock
                    {
                        Text = name,
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brushes.Black,
                    },
                },
            }
        );
    }
    var surface = new Border { Background = Brushes.White, Child = gallery };
    surface.Measure(new Size(576, 280));
    surface.Arrange(new Rect(0, 0, 576, 280));
    using var bitmap = new RenderTargetBitmap(new PixelSize(576, 280), new Vector(96, 96));
    bitmap.Render(surface);
    bitmap.Save(Path.Combine(output, "material-icons.png"), new PngBitmapEncoderOptions());
}
if (output is not null)
{
    Capture(
        new LoginView { DataContext = new LoginViewModel { ServerUrl = "http://127.0.0.1:5210" } },
        320,
        640,
        "login-compact"
    );
    Capture(
        new LoginView { DataContext = new LoginViewModel { ServerUrl = "http://127.0.0.1:5210" } },
        390,
        844,
        "login-narrow"
    );
    Capture(
        new LoginView { DataContext = new LoginViewModel { ServerUrl = "http://127.0.0.1:5210" } },
        1000,
        844,
        "login-wide"
    );
    var context = new ClientPluginContext
    {
        Client = new MessengerClient("http://localhost", new CourseFixture()),
        RefreshWorkspace = () => { },
        ShowMessage = _ => { },
    };
    context.Client.Restore(
        new Q.Sock.Shared.Models.Auth.AuthResponse
        {
            AccessToken = "fixture-token",
            SessionId = Guid.NewGuid(),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
        }
    );
    Capture(new LessonPlanningView(context), 390, 844, "lesson-narrow");
    Capture(new LessonPlanningView(context), 1000, 844, "lesson-wide");
    Capture(
        new SettingsView { DataContext = new SettingsViewModel() },
        390,
        1000,
        "settings-narrow"
    );
    Capture(new SettingsView { DataContext = new SettingsViewModel() }, 600, 1000, "settings-wide");
    Capture(
        new AppearanceView
        {
            DataContext = new AppearanceViewModel(
                new MessengerTheme(),
                _ => Task.CompletedTask,
                () => Task.CompletedTask
            ),
        },
        390,
        1000,
        "appearance-narrow"
    );
    var workspace = new WorkspaceView();
    workspace.SetLayout(1280, false, false);
    Capture(workspace, 1280, 820, "workspace-wide");
    workspace = new WorkspaceView();
    workspace.SetLayout(390, false, false);
    Capture(workspace, 390, 844, "workspace-narrow");

    var palette = new WrapPanel
    {
        Margin = new Thickness(24),
        ItemSpacing = 20,
        LineSpacing = 20,
    };
    for (var index = 0; index < AvatarColors.Palette.Count; index++)
    {
        var sample = UiFactory.Avatar("М", 56);
        sample.Background = Brush.Parse(AvatarColors.Palette[index]);
        palette.Children.Add(sample);
    }
    Capture(palette, 640, 340, "avatar-palette");
    var sampleId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    var stableAvatar = UiFactory.Avatar("Алексей", sampleId, AvatarKind.User);
    Check(
        ((SolidColorBrush)stableAvatar.Background!).Color
            == Color.Parse(AvatarColors.For(sampleId, AvatarKind.User)),
        "UI должен использовать цвет ID без перекрытия цветом темы."
    );
    Check(
        new MoodleClientPlugin().GetActions(context).Count == 0,
        "Учебные действия не должны появляться без серверного модуля."
    );
}

void Capture(Control view, int width, int height, string name)
{
    var host = new Border
    {
        Background = Brush.Parse("#313338"),
        Padding = name.StartsWith("lesson") ? new Thickness(24) : default,
    };
    if (name.StartsWith("lesson") || name.StartsWith("settings") || name.StartsWith("appearance"))
    {
        host.Styles.Add(new MessengerStyles());
        MessengerStyles.ApplyPalette(host, new MessengerTheme());
        host.Child = new ScrollViewer
        {
            Content = view,
            HorizontalScrollBarVisibility = Avalonia
                .Controls
                .Primitives
                .ScrollBarVisibility
                .Disabled,
        };
    }
    else
    {
        if (name.StartsWith("workspace") || name.StartsWith("login"))
        {
            host.Styles.Add(new MessengerStyles());
            MessengerStyles.ApplyPalette(host, new MessengerTheme());
        }
        host.Child = view;
    }
    var window = new Window
    {
        Content = host,
        Width = width,
        Height = height,
    };
    window.Show();
    Dispatcher.UIThread.RunJobs();
    window.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    foreach (var input in view.GetVisualDescendants().OfType<TextBox>())
    {
        var position = input.TranslatePoint(default, window);
        if (input.IsVisible && position is { } point)
            Check(
                point.X >= -1 && point.X + input.Bounds.Width <= width + 1,
                $"{name}: поле вышло за ширину окна."
            );
    }
    using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
    bitmap.Render(host);
    bitmap.Save(Path.Combine(output!, name + ".png"), new PngBitmapEncoderOptions());
    window.Close();
    Dispatcher.UIThread.RunJobs();
}

Console.WriteLine(
    $"PASS: {MaterialIcon.Names.Count} MUI geometries, five styles, fill holes, Two Tone alpha, SVG transforms, UI icons and plain-text shortcodes in Markdown."
);

static byte[] Render(string name)
{
    var icon = new MaterialIcon(name, 96) { Foreground = Brushes.White };
    icon.Measure(new Size(96, 96));
    icon.Arrange(new Rect(0, 0, 96, 96));
    using var bitmap = new RenderTargetBitmap(new PixelSize(96, 96), new Vector(96, 96));
    bitmap.Render(icon);
    var pixels = new byte[96 * 96 * 4];
    var pointer = Marshal.AllocHGlobal(pixels.Length);
    try
    {
        bitmap.CopyPixels(new PixelRect(0, 0, 96, 96), pointer, pixels.Length, 96 * 4);
        Marshal.Copy(pointer, pixels, 0, pixels.Length);
        return pixels;
    }
    finally
    {
        Marshal.FreeHGlobal(pointer);
    }
}

static byte Alpha(byte[] pixels, int x, int y) => pixels[(y * 96 + x) * 4 + 3];

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class CourseFixture : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"Проектирование ИС\",\"roomId\":\"22222222-2222-2222-2222-222222222222\"}]",
                    System.Text.Encoding.UTF8,
                    "application/json"
                ),
            }
        );
}
