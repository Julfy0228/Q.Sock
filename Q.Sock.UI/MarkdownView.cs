using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Q.Sock.Core;

namespace Q.Sock.UI;

public sealed class MarkdownView : StackPanel
{
    public MarkdownView(string markdown)
    {
        Spacing = 6;
        foreach (var block in Markdown.Parse(markdown, MessageFormatting.Pipeline))
            AddBlock(block);
    }

    private void AddBlock(Block block)
    {
        if (block is FencedCodeBlock code)
        {
            var content = code.Lines.ToString();
            var text = new SelectableTextBlock
            {
                FontFamily = new FontFamily("Consolas, monospace"),
                TextWrapping = TextWrapping.Wrap,
            };
            text.Bind(SelectableTextBlock.ForegroundProperty, new DynamicResourceExtension("text"));
            foreach (var token in CodeSyntax.Tokenize(content, code.Info))
            {
                var run = new Run(token.Text);
                var color = token.Kind switch
                {
                    "keyword" => "#C792EA",
                    "string" => "#C3E88D",
                    "number" => "#F78C6C",
                    "comment" => "#A2A8BD",
                    _ => null,
                };
                if (color is not null)
                    run.Foreground = Brush.Parse(color);
                text.Inlines!.Add(run);
            }
            var copy = UiFactory.Button(
                "Копировать код",
                async () =>
                {
                    if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                        await clipboard.SetTextAsync(content);
                }
            );
            Children.Add(
                UiFactory.Card(UiFactory.Column(UiFactory.Text(code.Info ?? "Код", 11), text, copy))
            );
            return;
        }

        if (block is LeafBlock leaf)
        {
            var text = new SelectableTextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = block is HeadingBlock heading ? 26 - heading.Level * 2 : 14,
            };
            text.Bind(SelectableTextBlock.ForegroundProperty, new DynamicResourceExtension("text"));
            if (leaf.Inline is { } inline)
                AddInlines(text.Inlines!, inline);
            Children.Add(text);
            return;
        }

        if (block is ContainerBlock container)
            foreach (var child in container)
                AddBlock(child);
    }

    private static void AddInlines(InlineCollection target, ContainerInline container)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    target.Add(new Run(literal.Content.ToString()));
                    break;
                case CodeInline code:
                    target.Add(
                        new Run(code.Content)
                        {
                            FontFamily = new FontFamily("Consolas, monospace"),
                            Foreground = Brush.Parse("#B6D5FF"),
                        }
                    );
                    break;
                case LineBreakInline:
                    target.Add(new LineBreak());
                    break;
                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterChar == '+')
                        span.TextDecorations = TextDecorations.Underline;
                    else if (emphasis.DelimiterCount >= 2)
                        span.FontWeight = FontWeight.Bold;
                    else
                        span.FontStyle = FontStyle.Italic;
                    AddInlines(span.Inlines, emphasis);
                    target.Add(span);
                    break;
                case LinkInline link:
                    if (
                        Uri.TryCreate(link.Url, UriKind.Absolute, out var address)
                        && address.Scheme is "https" or "http"
                    )
                    {
                        var label = string.Concat(
                            link.OfType<LiteralInline>().Select(item => item.Content.ToString())
                        );
                        target.Add(
                            new InlineUIContainer(
                                new HyperlinkButton
                                {
                                    Content = label.Length == 0 ? address.Host : label,
                                    NavigateUri = address,
                                    Padding = new Avalonia.Thickness(0),
                                }
                            )
                        );
                    }
                    else
                        AddInlines(target, link);
                    break;
                case ContainerInline nested:
                    AddInlines(target, nested);
                    break;
            }
        }
    }
}
