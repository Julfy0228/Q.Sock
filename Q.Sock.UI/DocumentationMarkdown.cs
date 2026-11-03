using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Q.Sock.Core;

namespace Q.Sock.UI;

public sealed class DocumentationMarkdown : StackPanel, IDisposable
{
    private readonly List<Bitmap> images = [];
    private readonly List<Image> pendingImages = [];
    private readonly Dictionary<string, Control> anchors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, Task> navigate;
    private readonly DocumentationClient client;
    private readonly Uri address;

    public DocumentationMarkdown(
        string markdown,
        DocumentationClient client,
        Uri address,
        Func<string, Task> navigate
    )
    {
        this.client = client;
        this.address = address;
        this.navigate = navigate;
        Spacing = 0;
        foreach (var block in Markdown.Parse(markdown, MessageFormatting.Pipeline))
            AddBlock(this, block);
    }

    public void ScrollTo(string fragment)
    {
        var key = Uri.UnescapeDataString(fragment.TrimStart('#'));
        if (anchors.TryGetValue(key, out var heading))
            heading.BringIntoView();
    }

    private void AddBlock(StackPanel target, Block block)
    {
        if (block is ListBlock list)
        {
            var listPanel = new StackPanel();
            var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
            foreach (var item in list.OfType<ListItemBlock>())
            {
                var row = new Grid { ColumnDefinitions = new("Auto,*") };
                var task = item.OfType<ParagraphBlock>()
                    .FirstOrDefault()
                    ?.Inline?.OfType<TaskList>()
                    .FirstOrDefault();
                if (task is not null)
                    row.Children.Add(
                        new CheckBox
                        {
                            IsChecked = task.Checked,
                            IsHitTestVisible = false,
                            Focusable = false,
                            MinHeight = 32,
                            Height = 32,
                            Padding = new Thickness(0),
                            Margin = new Thickness(0, 0, 5, 0),
                            VerticalContentAlignment = VerticalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Top,
                        }
                    );
                else
                    row.Children.Add(
                        new TextBlock
                        {
                            Text = list.IsOrdered ? $"{number++}. " : "• ",
                            FontSize = 14,
                            LineHeight = 24,
                        }
                    );
                var content = new StackPanel
                {
                    VerticalAlignment = task is null
                        ? VerticalAlignment.Top
                        : VerticalAlignment.Center,
                };
                Grid.SetColumn(content, 1);
                foreach (var child in item)
                    AddBlock(content, child);
                row.Children.Add(content);
                listPanel.Children.Add(row);
            }
            target.Children.Add(listPanel);
            return;
        }
        if (block is FencedCodeBlock code)
        {
            target.Children.Add(
                new SelectableTextBlock
                {
                    Text = code.Lines.ToString(),
                    FontFamily = new("Consolas, monospace"),
                    TextWrapping = TextWrapping.Wrap,
                }
            );
            return;
        }
        if (block is LeafBlock leaf)
        {
            if (
                leaf.Inline?.FirstChild is LinkInline picture
                && picture.NextSibling is null
                && (picture.IsImage || picture.FirstChild is LinkInline { IsImage: true })
            )
            {
                var illustration = picture.IsImage ? CreateImage(picture) : CreateLink(picture);
                illustration.HorizontalAlignment = HorizontalAlignment.Left;
                target.Children.Add(illustration);
                return;
            }
            var text = new SelectableTextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = block is HeadingBlock heading
                    ? Math.Max(17, 30 - heading.Level * 3)
                    : 14,
                FontWeight = block is HeadingBlock ? FontWeight.SemiBold : FontWeight.Normal,
                LineHeight = block is HeadingBlock ? double.NaN : 24,
                Margin =
                    block is HeadingBlock
                        ? new Thickness(0, target.Children.Count == 0 ? 0 : 12, 0, 6)
                        : new Thickness(0),
            };
            text.Bind(SelectableTextBlock.ForegroundProperty, new DynamicResourceExtension("text"));
            if (leaf.Inline is { } inline)
                AddInlines(text.Inlines!, inline);
            if (block is HeadingBlock)
            {
                if (block.GetAttributes().Id is { Length: > 0 } key)
                    anchors[key] = text;
            }
            target.Children.Add(text);
            return;
        }
        if (block is ContainerBlock container)
            foreach (var child in container)
                AddBlock(target, child);
    }

    private void AddInlines(InlineCollection target, ContainerInline container)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    target.Add(new Run(literal.Content.ToString()));
                    break;
                case CodeInline code:
                    target.Add(new Run(code.Content) { FontFamily = new("Consolas, monospace") });
                    break;
                case LineBreakInline:
                    target.Add(new LineBreak());
                    break;
                case TaskList:
                    break;
                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterCount >= 2)
                        span.FontWeight = FontWeight.Bold;
                    else
                        span.FontStyle = FontStyle.Italic;
                    AddInlines(span.Inlines, emphasis);
                    target.Add(span);
                    break;
                case LinkInline link when link.IsImage:
                    target.Add(new InlineUIContainer(CreateImage(link)));
                    break;
                case LinkInline link:
                    target.Add(
                        new InlineUIContainer(CreateLink(link))
                        {
                            BaselineAlignment = BaselineAlignment.Baseline,
                        }
                    );
                    break;
                case ContainerInline nested:
                    AddInlines(target, nested);
                    break;
            }
        }
    }

    private Button CreateLink(LinkInline link)
    {
        Control content;
        if (link.FirstChild is LinkInline { IsImage: true } image)
            content = CreateImage(image);
        else
        {
            var label = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                LineHeight = 24,
            };
            AddInlines(label.Inlines!, link);
            content = label;
        }
        var button = new DocumentationLink(content)
        {
            Content = content,
            FontSize = 14,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            BorderThickness = new Thickness(0),
            MinHeight = 0,
            MinWidth = 0,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        button.Classes.Add("documentation-link");
        if (content is TextBlock text)
            text.Bind(
                TextBlock.ForegroundProperty,
                button.GetObservable(Button.ForegroundProperty)
            );
        button.Click += async (_, _) => await navigate(link.Url ?? "");
        return button;
    }

    private Control CreateImage(LinkInline link)
    {
        var image = new Image
        {
            MaxWidth = 250,
            MaxHeight = 430,
            Stretch = Stretch.Uniform,
        };
        var holder = new StackPanel { Spacing = 4 };
        holder.Children.Add(image);
        var label = UiFactory.Text(
            string.Concat(link.OfType<LiteralInline>().Select(item => item.Content.ToString())),
            12,
            "secondary"
        );
        holder.Children.Add(label);
        image.Tag = (link.Url ?? "", label);
        pendingImages.Add(image);
        return holder;
    }

    public async Task LoadImages(CancellationToken cancellationToken)
    {
        foreach (var image in pendingImages)
        {
            if (image.Tag is not ValueTuple<string, TextBlock> data)
                continue;
            try
            {
                var resource = await client.Read(
                    client.Resolve(data.Item1, address),
                    cancellationToken
                );
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(resource.Bytes);
                var bitmap = new Bitmap(stream);
                images.Add(bitmap);
                image.Source = bitmap;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
                when (error
                        is System.Net.Http.HttpRequestException
                            or ArgumentException
                            or InvalidDataException
                )
            {
                data.Item2.Text += " — изображение недоступно";
            }
        }
    }

    public void Dispose()
    {
        foreach (var image in images)
            image.Dispose();
        images.Clear();
    }

    private sealed class DocumentationLink(Control content) : Button
    {
        protected override Type StyleKeyOverride => typeof(Button);

        protected override Size MeasureOverride(Size availableSize)
        {
            var size = base.MeasureOverride(availableSize);
            if (content is TextBlock label && label.TextLayout.TextLines.Count > 0)
                TextBlock.SetBaselineOffset(this, label.TextLayout.TextLines[0].Baseline);
            return size;
        }
    }
}
