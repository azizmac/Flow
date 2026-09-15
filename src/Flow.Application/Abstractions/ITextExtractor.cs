namespace Flow.Application.Abstractions;

/// <summary>
/// Достаёт текст из вложенного файла, чтобы его можно было проиндексировать наравне с задачами
/// и комментариями. Реализация живёт в Flow.Infrastructure (Search/Extraction): форматы — это
/// библиотеки и их особенности, Application про них знать не должен.
/// </summary>
/// <remarks>
/// Вложений в домене пока нет (нужно их ТЗ), поэтому вызывать извлекатель некому: он готов к тому
/// моменту, когда появятся файлы, и проверяется собственными тестами. Подключение — одна ветка
/// в <c>SearchSourceReader</c> для <c>SearchSourceType.Attachment</c>.
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
