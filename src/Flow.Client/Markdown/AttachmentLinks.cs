using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace Flow.Client.Markdown;

/// <summary>
/// Схема <c>attachment:{id}</c> в тексте описания и комментариев (docs/TZ_attachments.md).
/// Путь к файлу в текст не пишется намеренно: переезд хранилища или смена адреса API не должны ломать
/// уже написанные комментарии — ссылка называет вложение, а не место, где оно лежит.
///
/// Разметка сразу несёт настоящий адрес <c>/files/{id}</c> — дорисовывать её после рендера больше
/// не нужно. Этот маршрут аутентифицируется cookie (схема-диспетчер <c>AuthConstants.SmartScheme</c>:
/// под <c>/api</c> только Bearer, вне — cookie), а её браузер отправляет сам, без JS и без интеропа.
///
/// В тексте при этом по-прежнему хранится <c>attachment:{id}</c>: правится только узел AST при
/// рендере, дерево строится заново на каждый показ и выбрасывается. Писать путь в сам текст нельзя —
/// сломается проверка «файл упомянут в тексте» перед удалением (AttachmentList).
/// </summary>
public static class AttachmentLinks
{
    public const string Scheme = "attachment:";

    /// <summary>
    /// Признак вложения в разметке. Хуком для оживления быть перестал, но остался: по нему
    /// flow.js помечает не загрузившуюся картинку классом .missing, и по нему же удобно писать тесты.
    /// </summary>
    public const string Attribute = "data-attachment";

    /// <summary>Прямая ссылка на содержимое — FilesController, вне префикса /api.</summary>
    private static string Href(Guid id) => $"/files/{id}";

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
            link.Url = Href(id);
            // Лента комментариев бывает длинной: без lazy браузер запросит все картинки разом.
            attributes.AddProperty("loading", "lazy");
            attributes.AddProperty("decoding", "async");
            return;
        }

        // Обычная ссылка на скачивание. download обязателен по двум причинам: без него роутер
        // Blazor перехватит клик и попробует найти маршрут /files/{id} среди страниц, а при
        // протухшей cookie браузер сохранил бы под именем файла страницу входа, куда ведёт редирект.
        link.Url = Href(id);
        attributes.AddProperty("download", null);
        attributes.AddClass("md-att-file");
    }
}
