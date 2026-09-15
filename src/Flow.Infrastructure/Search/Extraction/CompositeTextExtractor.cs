using Flow.Application.Abstractions;
using Flow.Application.Features.Search;
using Microsoft.Extensions.Logging;

namespace Flow.Infrastructure.Search.Extraction;

/// <summary>
/// Точка входа для индексации: выбирает извлекатель по формату, режет результат по лимиту и гасит
/// ошибки. Битое или защищённое паролем вложение — это пустой текст и запись в лог, а не упавший
/// проход индексации: из-за одного файла не должна вставать очередь.
/// </summary>
internal sealed class CompositeTextExtractor(
    IEnumerable<ITextExtractor> extractors,
    SearchOptions options,
    ILogger<CompositeTextExtractor> logger) : ITextExtractor
{
    private readonly ITextExtractor[] _extractors = extractors.ToArray();

    public bool CanExtract(string fileName, string? contentType) =>
        _extractors.Any(extractor => extractor.CanExtract(fileName, contentType));

    public async Task<string> ExtractAsync(Stream content, string fileName, string? contentType, CancellationToken cancellationToken)
    {
        var extractor = _extractors.FirstOrDefault(candidate => candidate.CanExtract(fileName, contentType));
        if (extractor is null)
        {
            logger.LogDebug("Формат {File} не поддерживается — вложение не индексируется.", fileName);
            return string.Empty;
        }

        string text;
        try
        {
            text = await extractor.ExtractAsync(content, fileName, contentType, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Не удалось извлечь текст из {File}.", fileName);
            return string.Empty;
        }

        var limit = Math.Max(1000, options.Indexing.MaxDocumentChars);
        if (text.Length <= limit)
            return text;

        // Длинный документ режется по лимиту: договор на 300 страниц дал бы сотни чанков и столько же
        // обращений к модели, а найтись он должен и по началу.
        logger.LogInformation("Текст {File} обрезан с {Length} до {Limit} символов.", fileName, text.Length, limit);
        return text[..limit];
    }
}
