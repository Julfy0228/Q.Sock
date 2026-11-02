using System.Net.Http;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Q.Sock.Core;

namespace Q.Sock.UI;

public sealed class DocumentationView : UserControl, IDisposable
{
    private static readonly HttpClient Http = new();
    private readonly DocumentationClient client;
    private readonly HashSet<string> modules;
    private readonly ComboBox pages = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        MinWidth = 0,
    };
    private readonly TextBlock status = UiFactory.Text("Загрузка руководства…", 12, "secondary");
    private readonly ScrollViewer scroll = new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
    };
    private readonly Stack<Uri> history = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly Button back;
    private CancellationTokenSource? request;
    private DocumentationMarkdown? article;
    private Uri? current;
    private bool selecting;

    public DocumentationView(
        IClientPreferences preferences,
        IEnumerable<string>? modules = null,
        DocumentationClient? client = null
    )
    {
        this.client = client ?? new DocumentationClient(Http, preferences);
        this.modules = new(modules ?? [], StringComparer.OrdinalIgnoreCase);
        var grid = new Grid { RowDefinitions = new("Auto,Auto,*"), RowSpacing = 10 };
        var tools = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 6 };
        back = UiFactory.Button("Назад", GoBack);
        back.IsEnabled = false;
        tools.Children.Add(back);
        Grid.SetColumn(pages, 1);
        tools.Children.Add(pages);
        var reload = UiFactory.Button(
            "Обновить",
            () => Open(current ?? this.client.Resolve("overview.md"), false)
        );
        Grid.SetColumn(reload, 2);
        tools.Children.Add(reload);
        grid.Children.Add(tools);
        Grid.SetRow(status, 1);
        grid.Children.Add(status);
        Grid.SetRow(scroll, 2);
        grid.Children.Add(scroll);
        Content = grid;
        pages.SelectionChanged += async (_, _) =>
        {
            if (!selecting && pages.SelectedItem is ComboBoxItem { Tag: DocumentationPage page })
                await Open(this.client.Resolve(page.Path));
        };
        DetachedFromVisualTree += (_, _) => Dispose();
    }

    public async Task Initialize()
    {
        try
        {
            var index = await client.LoadIndex(lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            selecting = true;
            pages.Items.Clear();
            foreach (
                var page in index.Pages.Where(page =>
                    page.RequiresModule is null || modules.Contains(page.RequiresModule)
                )
            )
                pages.Items.Add(new ComboBoxItem { Content = page.Title, Tag = page });
            selecting = false;
            await Open(client.Resolve(index.Home));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
            when (error
                    is HttpRequestException
                        or InvalidDataException
                        or System.Text.Json.JsonException
                        or ArgumentException
            )
        {
            status.Text = error.Message;
        }
    }

    public async Task Navigate(string target)
    {
        try
        {
            var address = new Uri(current ?? client.Origin, target);
            if (client.IsInternal(address))
                await Open(address);
            else if (address.Scheme is "https" or "http")
                await Avalonia
                    .Controls.TopLevel.GetTopLevel(this)!
                    .Launcher.LaunchUriAsync(address);
        }
        catch (Exception error) when (error is ArgumentException or HttpRequestException)
        {
            status.Text = error.Message;
        }
    }

    private async Task GoBack()
    {
        if (history.TryPop(out var address))
            await Open(address, false);
        back.IsEnabled = history.Count > 0;
    }

    private async Task Open(Uri address, bool remember = true)
    {
        if (!address.AbsolutePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            status.Text = "Эта ссылка не ведёт на статью руководства.";
            return;
        }
        if (
            current?.GetLeftPart(UriPartial.Path) == address.GetLeftPart(UriPartial.Path)
            && address.Fragment.Length > 0
        )
        {
            article?.ScrollTo(address.Fragment);
            return;
        }
        request?.Cancel();
        request?.Dispose();
        request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = request.Token;
        status.Text = "Загрузка…";
        try
        {
            var resource = await client.Read(address, token);
            token.ThrowIfCancellationRequested();
            if (remember && current is not null && current != address)
                history.Push(current);
            current = address;
            back.IsEnabled = history.Count > 0;
            article?.Dispose();
            var rendered = new DocumentationMarkdown(
                Encoding.UTF8.GetString(resource.Bytes),
                client,
                address,
                Navigate
            );
            article = rendered;
            scroll.Content = rendered;
            scroll.Offset = new Vector(0, 0);
            selecting = true;
            pages.SelectedItem = pages
                .Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item =>
                    item.Tag is DocumentationPage page
                    && client.Resolve(page.Path).AbsolutePath == address.AbsolutePath
                );
            selecting = false;
            status.Text = resource.FromCache
                ? "Нет сети. Показана сохранённая копия."
                : "Руководство загружено";
            await rendered.LoadImages(token);
            if (!token.IsCancellationRequested)
                rendered.ScrollTo(address.Fragment);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
            when (error is HttpRequestException or InvalidDataException or FormatException)
        {
            status.Text = error.Message;
        }
    }

    public void Dispose()
    {
        request?.Cancel();
        lifetime.Cancel();
        article?.Dispose();
    }
}
