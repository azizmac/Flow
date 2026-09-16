using Flow.Application.Features.Search;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Flow.Infrastructure.Search.Extraction;

/// <summary>
/// Страницы PDF, из которых не извлёкся текст, — сканы. Для визуального поиска нужна картинка страницы,
/// и у скана она уже есть внутри файла: страница такого PDF — это один растр, вставленный целиком.
/// Поэтому здесь не рендер, а извлечение вложенного изображения — PdfPig уже в зависимостях,
/// а настоящий растеризатор (PDFium) притащил бы нативные библиотеки в образ ради того же результата.
///
/// Чего это не покрывает: PDF, нарисованный векторами и без текстового слоя, — картинки внутри нет,
/// извлекать нечего. Такие файлы остаются в индексе только по имени, как и раньше.
/// </summary>
internal sealed class PdfPageImageExtractor(SearchOptions options, ILogger<PdfPageImageExtractor> logger)
{
    /// <summary>Сигнатура JPEG: изображение в PDF чаще всего лежит готовым файлом (фильтр DCTDecode).</summary>
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    public static bool IsPdf(string fileName, string? contentType) =>
        string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Картинки первых страниц: по одной на страницу, самая крупная. Пустой список — извлекать нечего,
    /// и это не ошибка: файл просто останется в индексе по имени и по тексту, если тот был.
    /// </summary>
    public async Task<IReadOnlyList<PdfPageImage>> ExtractAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, options.Embeddings.Vision.MaxPdfPages);

        try
        {
            var seekable = await Seekable.EnsureAsync(content, cancellationToken);
            using var document = PdfDocument.Open(seekable);

            var pages = new List<PdfPageImage>();
            foreach (var page in document.GetPages().Take(limit))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Largest(page) is { } image)
                    pages.Add(new PdfPageImage(image.Bytes, image.ContentType, page.Number));
            }

            return pages;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Битый или защищённый файл: визуальная половина обойдётся без него, индексация не встаёт.
            logger.LogWarning(ex, "Не удалось достать страницы {File} для визуального индекса.", fileName);
            return [];
        }
    }

    /// <summary>
    /// Самое крупное изображение страницы. У скана оно одно и на всю страницу; у обычного PDF с текстом
    /// сюда попал бы логотип — но такие файлы до визуальной ветки не доходят, у них есть текст.
    /// </summary>
    private static (byte[] Bytes, string ContentType)? Largest(Page page)
    {
        (byte[] Bytes, string ContentType)? best = null;
        var bestArea = 0L;

        foreach (var image in page.GetImages())
        {
            var area = (long)image.WidthInSamples * image.HeightInSamples;
            if (area <= bestArea)
                continue;

            if (Convert(image) is not { } converted)
                continue;

            best = converted;
            bestArea = area;
        }

        return best;
    }

    /// <summary>
    /// Байты в том виде, в каком их поймёт модель. JPEG внутри PDF хранится готовым файлом — его отдаём
    /// как есть; остальное PdfPig пересобирает в PNG. Экзотические цветовые пространства он не тянет —
    /// такая страница просто пропускается.
    /// </summary>
    private static (byte[] Bytes, string ContentType)? Convert(IPdfImage image)
    {
        if (image.TryGetBytesAsMemory(out var raw) && raw.Length > JpegSignature.Length
            && raw.Span[..JpegSignature.Length].SequenceEqual(JpegSignature))
        {
            return (raw.ToArray(), "image/jpeg");
        }

        return image.TryGetPng(out var png) && png is { Length: > 0 }
            ? (png, "image/png")
            : null;
    }
}

/// <param name="Number">Номер страницы с единицы — он попадает в текст чанка, чтобы в выдаче было видно, какая.</param>
internal sealed record PdfPageImage(byte[] Content, string ContentType, int Number);
