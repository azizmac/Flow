using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace Flow.Client.Markdown;

/// <summary>
/// Схема <c>attachment:{id}</c> в тексте описания и комментариев (docs/TZ_attachments.md).
/// Путь к файлу в текст не пишется намеренно: переезд хранилища или смена адреса API не должны ломать
/// уже написанные комментарии — ссылка называет вложение, а не место, где оно лежит.
///
/// Разметку готовим здесь, а подставляет содержимое уже компонент: файл отдаётся только по токену,
/// поэтому картинка получает <c>data-attachment</c> вместо <c>src</c>, а ссылка — вместо адреса.
/// </summary>
public static class AttachmentLinks
{
    public const string Scheme = "attachment:";

    /// <summary>Атрибут, по которому MarkdownView находит, что оживлять.</summary>
    public const string Attribute = "data-attachment";

    /// <summary>
    /// Прозрачный пиксель вместо настоящего src: пустой src браузер трактует как ссылку на саму
    /// страницу и повторно её запрашивает.
    /// </summary>
    private const string Placeholder = "data:image/gif;base64,R0lGODlhAQABAAAAACH5BAEKAAEALAAAAAABAAEAAAICTAEAOw==";

    /// <summary>
    /// Ссылка на вложение → разметка для оживления. Чужие схемы и мусор вместо id не трогаем:
    /// текст пишут люди, и «attachment:вчерашний файл» должно остаться просто текстом ссылки.
    /// </summary>
    public static void Rewrite(LinkInline link)
    {
        if (link.Url is not { } url || !url.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
            return;

        if (!Guid.TryParse(url[Scheme.Length..], out var id))
            return;

        var attributes = link.GetAttributes();
        attributes.AddProperty(Attribute, id.ToString());

        if (link.IsImage)
        {
            attributes.AddClass("md-att-img");
            link.Url = Placeholder;
            return;
        }

        // Адрес у ссылки остаётся исходным, «attachment:{id}»: клик перехватывает обработчик
        // (файл качается по токену), а если скрипт не успел навеситься — неизвестная схема просто
        // никуда не ведёт. «#» увёл бы страницу наверх, пустой href — перезагрузил бы её.
        attributes.AddClass("md-att-file");
    }
}
