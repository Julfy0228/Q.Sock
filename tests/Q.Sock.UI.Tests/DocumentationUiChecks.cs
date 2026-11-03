using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Q.Sock.Core;
using Q.Sock.UI;

internal static class DocumentationUiChecks
{
    public static void Run()
    {
        var preferences = new Preferences();
        using var http = new HttpClient(new Transport());
        var client = new DocumentationClient(http, preferences);
        using var markdown = new DocumentationMarkdown(
            "# Заголовок\n\n*Курсив*\n\n- [x] Готово\n- [ ] Проверить\n\n[Статья](guide/chats.md)",
            client,
            client.Resolve("overview.md"),
            _ => Task.CompletedTask
        );
        var text = markdown.Children.OfType<SelectableTextBlock>().ToArray();
        Check(
            text.SelectMany(item => item.Inlines ?? [])
                .OfType<Span>()
                .Any(span => span.FontStyle == FontStyle.Italic),
            "Курсив потерян."
        );
        var view = new DocumentationView(preferences, client: client) { Height = 640 };
        var host = new Border { Child = view };
        host.Styles.Add(new MessengerStyles());
        MessengerStyles.ApplyPalette(host, new MessengerTheme());
        var window = new Window
        {
            Width = 390,
            Height = 740,
            Content = host,
        };
        try
        {
            window.Show();
            view.Initialize().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Check(
                view.GetVisualDescendants().OfType<CheckBox>().Count() == 2,
                "Флажки списка не отрисованы."
            );
            Check(
                view.GetVisualDescendants()
                    .OfType<CheckBox>()
                    .All(box => box.Parent is Grid && Grid.GetColumn(box) == 0),
                "Флажки должны находиться в колонке маркеров списка."
            );
            Check(
                view.GetVisualDescendants().OfType<Image>().Any(image => image.Source is not null),
                "Изображение не загружено."
            );
            var selector = view.GetVisualDescendants().OfType<ComboBox>().Single();
            var link = view.GetVisualDescendants()
                .OfType<Button>()
                .First(button => button.Classes.Contains("documentation-link"));
            Check(link.FontSize == 14, "Ссылка должна использовать размер основного текста.");
            Check(
                ((ISolidColorBrush)link.Foreground!).Color
                    == ((ISolidColorBrush)host.Resources["link"]!).Color,
                "Ссылка должна применять цвет из палитры."
            );
            Check(
                selector.Bounds.Height == 40,
                "Высота выбора статьи должна совпадать с кнопками."
            );
            Check(selector.Items.Count == 2, "Статья расширения видна без подключённого модуля.");
            view.Navigate("guide/chats.md").GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Check(
                view.GetVisualDescendants()
                    .OfType<SelectableTextBlock>()
                    .Any(block =>
                        block.Text == "Чаты"
                        || block.Inlines?.OfType<Run>().Any(run => run.Text == "Чаты") == true
                    ),
                "Внутренняя ссылка не открыла статью."
            );
            Check(
                view.GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button => Equals(button.Content, "Назад"))
                    .IsEnabled,
                "История переходов не заполнена."
            );
            Check(view.Bounds.Width <= 390, "Справка выходит за узкое окно.");
        }
        finally
        {
            window.Close();
            view.Dispose();
        }
        Console.WriteLine(
            "PASS: native documentation italics, checklist, image, internal navigation, history, plugin filtering and narrow layout."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Preferences : IClientPreferences
    {
        public Task<string?> Read(string key) => Task.FromResult<string?>(null);

        public Task Write(string key, string value) => Task.CompletedTask;
    }

    private sealed class Transport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var path = request.RequestUri!.AbsolutePath;
            HttpContent content =
                path.EndsWith("navigation.json")
                    ? new StringContent(
                        "{\"version\":1,\"home\":\"overview.md\",\"pages\":[{\"path\":\"overview.md\",\"title\":\"Главная\"},{\"path\":\"guide/chats.md\",\"title\":\"Чаты\"},{\"path\":\"integrations/sample.md\",\"title\":\"Расширение\",\"requiresModule\":\"sample\"}]}"
                    )
                : path.EndsWith(".png") ? new ByteArrayContent(ImageBytes())
                : new StringContent(
                    path.EndsWith("chats.md")
                        ? "# Чаты"
                        : "# Справка\n\n*Курсив*\n\n- [x] Готово\n- [ ] Проверить\n\n[![Вход](assets/login.png)](guide/chats.md)"
                );
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = content }
            );
        }

        private static byte[] ImageBytes()
        {
            using var bitmap = new SkiaSharp.SKBitmap(2, 2);
            bitmap.Erase(SkiaSharp.SKColors.Blue);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }
}
