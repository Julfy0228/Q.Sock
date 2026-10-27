using Markdig;

namespace Q.Sock.Core;

public static class MessageFormatting
{
    public static MarkdownPipeline Pipeline { get; } =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

    public static string ToHtml(string markdown) => Markdown.ToHtml(markdown, Pipeline);
}
