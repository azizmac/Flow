using System.Globalization;
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
    /// <summary>
    /// Потолок высоты картинки в тексте. Обязан совпадать с max-height у .md-att-img
    /// (src/Flow.Api/wwwroot/css/app.css) — при расхождении разметка и стили тихо разъедутся.
    /// </summary>
    private const int MaxBoxHeight = 420;

    /// <summary>Рамка .md-att-img, 1px с каждой стороны. Вычитается, потому что box-sizing: border-box.</summary>
    private const int Borders = 2;

    /// <param name="sizes">
    /// Отображаемые размеры вложения по его id; null — справочника нет, атрибуты не пишутся
    /// и поведение остаётся прежним. См. Services/AttachmentSizes.
    /// </param>
    public static void Rewrite(LinkInline link, Func<Guid, (int Width, int Height)?>? sizes = null)
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
            // Но одного lazy мало: без размеров незагруженная картинка занимает 2x2 пикселя,
            // вся лента помещается в первый экран, и откладывать оказывается нечего.
            attributes.AddProperty("loading", "lazy");
            attributes.AddProperty("decoding", "async");
            AddSize(attributes, sizes?.Invoke(id));
            return;
        }

        // Обычная ссылка на скачивание. download обязателен по двум причинам: без него роутер
        // Blazor перехватит клик и попробует найти маршрут /files/{id} среди страниц, а при
        // протухшей cookie браузер сохранил бы под именем файла страницу входа, куда ведёт редирект.
        link.Url = Href(id);
        attributes.AddProperty("download", null);
        attributes.AddClass("md-att-file");
    }

    /// <summary>
    /// Пара width/height плюс инлайновый потолок ширины.
    ///
    /// Потолок не украшение: когда ширина задана, max-height режет высоту, НЕ пересчитывая ширину, —
    /// и обычное фото 4:3 растягивается. Считается он по контентной коробке (отсюда минус рамка),
    /// а min(100%, …) оставляет за max-width право ужать картинку до колонки.
    ///
    /// Если автор текста задал размеры сам (в пайплайне включены generic attributes: «![x](…){width=50}»),
    /// не пишем ничего. При дубле парсер HTML оставляет ПЕРВЫЙ атрибут и выбрасывает второй — вышла бы
    /// чужая ширина с нашей высотой, то есть гарантированно неверное соотношение сторон.
    /// </summary>
    private static void AddSize(HtmlAttributes attributes, (int Width, int Height)? size)
    {
        if (size is not ({ } width, { } height) || width <= 0 || height <= 0 || HasOwnSize(attributes))
            return;

        attributes.AddProperty("width", width.ToString(CultureInfo.InvariantCulture));
        attributes.AddProperty("height", height.ToString(CultureInfo.InvariantCulture));

        // Нижний зажим: у полоски вроде 200x5000 потолок выродился бы в доли пикселя, а у подделанного
        // заголовка — в ноль, и картинка исчезла бы совсем. InvariantCulture обязателен: под русской
        // локалью дробное число ушло бы в CSS с запятой.
        var cap = Math.Max(24.0, (MaxBoxHeight - Borders) * (double)width / height + Borders);
        attributes.AddProperty(
            "style",
            $"max-width:min(100%,{cap.ToString("0.##", CultureInfo.InvariantCulture)}px)");
    }

    private static bool HasOwnSize(HtmlAttributes attributes) =>
        attributes.Properties?.Any(p =>
            p.Key.Equals("width", StringComparison.OrdinalIgnoreCase) ||
            p.Key.Equals("height", StringComparison.OrdinalIgnoreCase) ||
            p.Key.Equals("style", StringComparison.OrdinalIgnoreCase)) == true;
}
