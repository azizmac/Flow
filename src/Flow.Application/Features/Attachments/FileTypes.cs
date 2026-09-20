namespace Flow.Application.Features.Attachments;

/// <summary>
/// Тип файла определяет сервер, а не браузер: заголовок из запроса подделывается тривиально, а от типа
/// зависит, покажем ли мы файл прямо в интерфейсе. Чистые функции — проверяются юнит-тестами.
/// </summary>
public static class FileTypes
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".csv"] = "text/csv",
        [".log"] = "text/plain",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".zip"] = "application/zip",
        [".7z"] = "application/x-7z-compressed",
        [".rar"] = "application/vnd.rar",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation"
    };

    public const string Fallback = "application/octet-stream";

    public static string Extension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot >= 0 && dot < fileName.Length - 1 ? fileName[dot..].ToLowerInvariant() : string.Empty;
    }

    /// <summary>Тип по расширению; неизвестное расширение — нейтральный поток байтов.</summary>
    public static string FromFileName(string fileName) =>
        ByExtension.TryGetValue(Extension(fileName), out var contentType) ? contentType : Fallback;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    /// <summary>
    /// Проверяет, что содержимое действительно того типа, за который себя выдаёт расширение.
    /// Применяется только к тем типам, которые мы готовы показывать inline: .png, внутри которого
    /// лежит HTML, картинкой стать не должен. Для остальных типов сигнатуру не сверяем — файл
    /// всё равно уйдёт на скачивание.
    /// </summary>
    public static bool MatchesSignature(string contentType, ReadOnlySpan<byte> head) => contentType switch
    {
        "image/png" => head.StartsWith(PngSignature),
        "image/jpeg" => head.StartsWith(JpegSignature),
        "image/gif" => head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8),
        // RIFF....WEBP: между сигнатурой и меткой формата лежит длина файла.
        "image/webp" => head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8),
        // Не true. Сюда попадают только типы из InlineContentTypes, а он биндится из конфигурации:
        // Attachments__InlineContentTypes__0=image/svg+xml меняет список без пересборки и без ревью.
        // С «_ => true» такой тип прошёл бы вообще без проверки содержимого и стал бы показываться
        // прямо в браузере с нашего origin. С «_ => false» он понижается до Fallback и может только
        // скачиваться: чтобы показывать тип inline, для него обязана существовать ветка выше.
        _ => false
    };

    /// <summary>Сколько байт нужно увидеть, чтобы проверить сигнатуру любого из inline-типов.</summary>
    public const int SignatureLength = 12;
}
