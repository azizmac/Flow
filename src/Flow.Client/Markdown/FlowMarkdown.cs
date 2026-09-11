using Markdig;

namespace Flow.Client.Markdown;

/// <summary>
/// Один пайплайн на клиент: описание задачи, комментарии, предпросмотр в редакторе рендерятся одинаково.
/// Сырой HTML отключён — текст приходит от пользователей и не должен превращаться в разметку.
/// </summary>
public static class FlowMarkdown
{
    public static readonly MarkdownPipeline Pipeline = Build();

    public static string ToHtml(string? text) => Markdig.Markdown.ToHtml(text ?? string.Empty, Pipeline);

    private static MarkdownPipeline Build()
    {
        var builder = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .DisableHtml();

        builder.InlineParsers.Insert(0, new MentionInlineParser());
        return builder.Build();
    }
}
