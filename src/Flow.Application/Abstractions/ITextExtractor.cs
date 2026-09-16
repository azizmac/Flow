namespace Flow.Application.Abstractions;

/// <summary>
/// Достаёт текст из вложенного файла, чтобы его можно было проиндексировать наравне с задачами
/// и комментариями. Реализация живёт в Flow.Infrastructure (Search/Extraction): форматы — это
/// библиотеки и их особенности, Application про них знать не должен.
/// </summary>
/// <remarks>
/// Вызывается из <c>SearchSourceReader</c> для <c>SearchSourceType.Attachment</c>: файл тянется
/// из хранилища, текст режется на чанки наравне с описанием задачи.
/// </remarks>
public interface ITextExtractor
{
    /// <summary>Возьмётся ли извлекатель за этот файл — решается по расширению и content-type.</summary>
    bool CanExtract(string fileName, string? contentType);

    /// <summary>
    /// Текст файла. Пустая строка — извлекать нечего: скан без текстового слоя, пустой документ,
    /// неподдерживаемый или битый файл. Исключения наружу не выходят: одно испорченное вложение
    /// не должно останавливать индексацию остальных.
    /// </summary>
    Task<string> ExtractAsync(Stream content, string fileName, string? contentType, CancellationToken cancellationToken);
}
