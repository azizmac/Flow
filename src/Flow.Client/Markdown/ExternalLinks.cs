using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace Flow.Client.Markdown;

/// <summary>
/// Обычные ссылки в описаниях и комментариях. Клиент — SPA: ссылку на свой домен Blazor перехватывает
/// и уводит роутер, поэтому «голый» адрес вроде <c>[сайт](example.com)</c> открывался не сайтом,
/// а пустой страницей Flow. Приводим такие адреса к внешним и открываем их в новой вкладке, чтобы
/// недописанный комментарий не потерялся вместе с текущей страницей.
///
/// Внутренние ссылки не трогаем: упоминания (<c>@username</c> → <c>u/…</c>), вложения
/// (<see cref="AttachmentLinks"/>), якоря <c>#…</c> и пути от корня <c>/…</c>.
/// </summary>
public static class ExternalLinks
{
    /// <summary>Схемы, которые в тексте задачи означают не переход, а выполнение кода: ссылкой не делаем.</summary>
    private static readonly string[] Dangerous = ["javascript:", "data:", "vbscript:", "file:"];

    /// <summary>Схемы, которые открывает не браузер, а почтовик или телефон: оставляем как есть.</summary>
    private static readonly string[] Handoff = ["mailto:", "tel:"];

    public static void Rewrite(LinkInline link)
    {
        if (link.IsImage || link.Url is not { Length: > 0 } url)
            return;

        // Якорь внутри текста и адрес внутри Flow — ссылки свои, роутер обработает их правильно.
        if (url[0] is '#' or '/')
            return;

        var attributes = link.GetAttributes();
        if (attributes.Classes is { } classes && (classes.Contains("mention") || classes.Contains("md-att-file")))
            return;

        if (Dangerous.Any(s => url.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
        {
            // Текст ссылки остаётся видимым, но никуда не ведёт: «#» не перезагружает страницу и не
            // выполняет ничего, в отличие от пустого href или исходной схемы.
            link.Url = "#";
            attributes.AddClass("md-link-blocked");
            return;
        }

        if (Handoff.Any(s => url.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
            return;

        var web = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                  || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        if (!web)
        {
            // Чужая схема (ftp: и прочее) — не наше дело, остальное считаем адресом в интернете:
            // «example.com» человек пишет, имея в виду сайт, а не страницу Flow.
            if (url.Contains("://", StringComparison.Ordinal))
                return;

            link.Url = "https://" + url;
        }

        // Новая вкладка: уход со страницы потерял бы и открытую задачу, и недописанный комментарий.
        // rel — обязательная пара к target=_blank: открытая страница не получает доступа к нашей.
        attributes.AddProperty("target", "_blank");
        attributes.AddProperty("rel", "noopener noreferrer");
    }
}
