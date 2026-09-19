using Flow.Application.Features.Attachments.Queries.AttachmentContentQuery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Flow.Api.Controllers;

/// <summary>
/// Общая сборка ответа с содержимым вложения. Входов два и они останутся разными:
/// <c>/api/attachments/{id}/content</c> — для клиентов API по Bearer, <c>/files/{id}</c> — для самих
/// страниц по cookie. А вот правила отдачи обязаны жить в одном месте: если они разойдутся, прямая
/// ссылка на загруженный .html или .svg превратится в XSS на собственном origin.
/// </summary>
internal static class AttachmentDelivery
{
    /// <summary>ETag от SHA-256 содержимого: он уже посчитан при загрузке и лежит в строке вложения.</summary>
    public static string ETagOf(byte[] contentHash) => $"\"{Convert.ToHexString(contentHash).ToLowerInvariant()}\"";

    public static bool Matches(ControllerBase controller, string etag) =>
        controller.Request.Headers.IfNoneMatch.Any(value => value == etag);

    /// <summary>
    /// Заголовки, общие для 200 и 304. Ставятся до выбора ветки: браузер должен получить их
    /// и вместе с ответом «не изменилось», иначе при следующем показе он потеряет и ETag, и правила кэша.
    /// </summary>
    public static void ApplyHeaders(
        ControllerBase controller,
        string fileName,
        bool canInline,
        bool forceDownload,
        string? etag)
    {
        var response = controller.Response;

        // Даже при inline браузер не должен угадывать тип по содержимому.
        response.Headers.XContentTypeOptions = "nosniff";

        // Два публичных имени Flow живут на одном регистрируемом домене, а SameSite=Lax различает
        // сайты, а не origin'ы: без CORP соседний сервис того же домена смог бы дёрнуть файл тегом img
        // с нашей cookie. Прочитать пиксели он бы не смог, но факт наличия файла и его размер — да.
        // ВАЖНО: CORP не проверяется для запросов, одобренных CORS. Добавить AllowCredentials
        // в политику FlowClient (Program.cs) — значит обнулить эту защиту целиком.
        response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";

        // Показывать в браузере можно только типы из белого списка (картинки). Всё остальное —
        // только скачивание: inline для произвольного типа это XSS на своём origin.
        var inline = canInline && !forceDownload;
        if (inline)
        {
            // Второй рубеж на случай, если белый список когда-нибудь разойдётся с проверкой сигнатур:
            // sandbox без токенов даёт opaque origin и запрещает скрипты, то есть переход по прямой
            // ссылке на inline-файл не сможет ничего выполнить на нашем origin. На ответ-вложение
            // не ставим: незачем, а в Chrome sandbox исторически конфликтует со скачиванием.
            response.Headers.ContentSecurityPolicy = "sandbox";
        }

        var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
        // FileNameStar кодирует имя по RFC 5987 — кириллица и пробелы доезжают целыми.
        disposition.SetHttpFileName(fileName);
        response.Headers.ContentDisposition = disposition.ToString();

        if (etag is null)
            return;

        response.Headers.ETag = etag;

        // no-cache, а НЕ max-age: удаление вложения сегодня действует мгновенно (это закреплено
        // тестом «DELETE → 404 на content»), и единственный способ отозвать доступ к файлу в Flow —
        // именно удаление. Суточный кэш его отменил бы: браузер показывал бы картинку из кэша ещё
        // сутки. no-cache оставляет тело в кэше, но требует спрашивать каждый раз — трафик экономится
        // теми же 304, а мгновенность удаления сохраняется.
        response.Headers.CacheControl = "private, no-cache";
    }

    /// <summary>Отдаёт поток. Заголовки к этому моменту уже проставлены ApplyHeaders.</summary>
    public static IActionResult Send(ControllerBase controller, AttachmentContent content)
    {
        var response = controller.Response;

        // Сжимать содержимое вложений незачем: картинки и архивы уже сжаты, а 25-мегабайтный .log
        // через Brotli в горячем пути — чистая потеря CPU. Плюс ResponseCompression обнуляет
        // Content-Length у типов из своего списка (туда попадают svg, json, xml, text/plain),
        // и ответ уходил бы chunked без прогресса. Явный identity заставляет middleware пропустить ответ.
        response.Headers.ContentEncoding = "identity";

        // Поток из S3 не seekable (GetObjectResponse.ResponseStream), поэтому MVC сам длину не узнает.
        // Длина уже известна из строки вложения — проставляем её руками.
        response.ContentLength = content.SizeBytes;

        // enableRangeProcessing здесь был бы обманом: диапазоны требуют seekable-потока, MVC на
        // несеекабельном их не отдаёт (ни Accept-Ranges, ни 206). Чтобы перемотка заработала,
        // Range надо прокидывать до S3 отдельной задачей.
        return controller.File(content.Content, content.ContentType);
    }
}
