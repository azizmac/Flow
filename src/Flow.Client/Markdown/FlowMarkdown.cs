using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
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

    private static readonly MarkdownPipeline AgentResponsePipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks()
        .UseTaskLists()
        .UseEmphasisExtras()
        .DisableHtml()
        .Build();

    /// <summary>
    /// Текст → HTML. Ссылки (вложения, внешние адреса) правятся в дереве, а не в готовой строке:
    /// подменять атрибуты регулярками по HTML — значит однажды попасть в текст пользователя, а не в разметку.
    /// </summary>
    /// <param name="sizes">
    /// Размеры картинок-вложений по id (Services/AttachmentSizes). null — как раньше: атрибуты
    /// width/height не пишутся, браузер места под картинку не резервирует.
    /// </param>
    public static string ToHtml(string? text, Func<Guid, (int Width, int Height)?>? sizes = null, bool forAgentResponse = false)
    {
        var pipeline = forAgentResponse ? AgentResponsePipeline : Pipeline;
        var document = Markdig.Markdown.Parse(text ?? string.Empty, pipeline);

        foreach (var link in document.Descendants<LinkInline>())
        {
            if (forAgentResponse)
                RewriteAgentLink(link);
            else
            {
                AttachmentLinks.Rewrite(link, sizes);
                ExternalLinks.Rewrite(link);
            }
        }

        if (forAgentResponse)
        {
            foreach (var link in document.Descendants<AutolinkInline>().ToArray())
            {
                if (link.IsEmail || !IsAgentLinkAllowed(link.Url))
                    link.ReplaceBy(new LiteralInline(link.Url));
                else
                    AddExternalLinkAttributes(link);
            }
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Render(document);
        return writer.ToString();
    }

    /// <summary>Ответ агента не должен загружать изображения или превращать произвольные схемы в переходы.</summary>
    private static void RewriteAgentLink(LinkInline link)
    {
        if (link.IsImage || !IsAgentLinkAllowed(link.Url))
        {
            // Оставляем подпись картинки/ссылки, но не создаём img и не отдаём адрес браузеру.
            link.IsImage = false;
            link.Url = "#";
            link.GetDynamicUrl = null;
            link.GetAttributes().AddClass("md-link-blocked");
            return;
        }

        if (link.Url![0] != '#')
            AddExternalLinkAttributes(link);
    }

    private static bool IsAgentLinkAllowed(string? url) =>
        !string.IsNullOrEmpty(url) && (url[0] == '#' ||
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");

    private static void AddExternalLinkAttributes(MarkdownObject link)
    {
        var attributes = link.GetAttributes();
        attributes.AddProperty("target", "_blank");
        attributes.AddProperty("rel", "noopener noreferrer");
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
