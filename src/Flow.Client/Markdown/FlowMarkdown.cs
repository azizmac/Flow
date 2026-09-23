using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Flow.Client.Markdown;

/// <summary>
/// Один пайплайн на клиент: описание задачи, комментарии, предпросмотр в редакторе рендерятся одинаково.
/// Сырой HTML отключён — текст приходит от пользователей и не должен превращаться в разметку.
/// </summary>
public static class FlowMarkdown
{
    public static readonly MarkdownPipeline Pipeline = Build();

    /// <summary>
    /// Текст → HTML. Ссылки (вложения, внешние адреса) правятся в дереве, а не в готовой строке:
    /// подменять атрибуты регулярками по HTML — значит однажды попасть в текст пользователя, а не в разметку.
    /// </summary>
    /// <param name="sizes">
    /// Размеры картинок-вложений по id (Services/AttachmentSizes). null — как раньше: атрибуты
    /// width/height не пишутся, браузер места под картинку не резервирует.
    /// </param>
    public static string ToHtml(string? text, Func<Guid, (int Width, int Height)?>? sizes = null)
    {
        var document = Markdig.Markdown.Parse(text ?? string.Empty, Pipeline);

        foreach (var link in document.Descendants<LinkInline>())
        {
            AttachmentLinks.Rewrite(link, sizes);
            ExternalLinks.Rewrite(link);
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        return writer.ToString();
    }

    private static MarkdownPipeline Build()
    {
        var builder = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .DisableHtml();

        builder.InlineParsers.Insert(0, new MentionInlineParser());
        return builder.Build();
    }
}
