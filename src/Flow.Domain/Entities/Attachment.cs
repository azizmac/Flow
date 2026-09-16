namespace Flow.Domain.Entities;

/// <summary>
/// Файл, приложенный к задаче (docs/TZ_attachments.md). Владелец всегда задача: комментарий
/// ссылается на вложение текстом, своей сущности у него нет.
/// Не редактируется — только создаётся и удаляется, поэтому мутаций у класса нет.
/// </summary>
public sealed class Attachment
{
    public const int FileNameMaxLength = 255;
    public const int ContentTypeMaxLength = 100;
    public const int StorageKeyMaxLength = 512;

    /// <summary>SHA-256 — 32 байта; по нему ловятся повторы в пределах одной задачи.</summary>
    private const int HashLength = 32;

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    /// <summary>Копия проекта задачи: по ней строится ключ объекта и фильтруется поиск.</summary>
    public Guid BoardId { get; private set; }

    /// <summary>Имя, как его принёс пользователь: показывается в списке и отдаётся при скачивании.</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>Тип, определённый сервером по расширению и сигнатуре, а не присланный браузером.</summary>
    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public byte[] ContentHash { get; private set; } = [];

    /// <summary>Ключ объекта в бакете. Имя файла в него не входит — только идентификаторы и расширение.</summary>
    public string StorageKey { get; private set; } = string.Empty;

    public Guid UploadedById { get; private set; }

    public DateTime UploadedAt { get; private set; }

    private Attachment()
    {
        // EF Core
    }

    public static Attachment Create(
        Guid taskId,
        Guid boardId,
        string fileName,
        string contentType,
        long sizeBytes,
        byte[] contentHash,
        Guid uploadedById)
    {
        if (taskId == Guid.Empty)
            throw new ArgumentException("Task id must not be empty.", nameof(taskId));
        if (boardId == Guid.Empty)
            throw new ArgumentException("Board id must not be empty.", nameof(boardId));
        if (uploadedById == Guid.Empty)
            throw new ArgumentException("Uploaded by id must not be empty.", nameof(uploadedById));
        if (sizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "Attachment must not be empty.");
        if (contentHash.Length != HashLength)
            throw new ArgumentException($"Content hash must be {HashLength} bytes (SHA-256).", nameof(contentHash));

        var name = Sanitize(fileName);
        if (name.Length == 0)
            throw new ArgumentException("File name must not be empty.", nameof(fileName));

        var id = Guid.NewGuid();

        return new Attachment
        {
            Id = id,
            TaskId = taskId,
            BoardId = boardId,
            FileName = name,
            ContentType = Trim(contentType, ContentTypeMaxLength) is { Length: > 0 } type ? type : "application/octet-stream",
            SizeBytes = sizeBytes,
            ContentHash = contentHash,
            StorageKey = BuildKey(boardId, taskId, id, name),
            UploadedById = uploadedById,
            UploadedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Ключ объекта: по префиксу видно, к какому проекту и задаче относится файл, а удаление проекта
    /// сводится к удалению префикса. Имя файла в ключ не попадает — кириллица, пробелы и «../» в нём
    /// сделали бы ключ ненадёжным.
    /// </summary>
    private static string BuildKey(Guid boardId, Guid taskId, Guid id, string fileName) =>
        TaskPrefix(boardId, taskId) + id + Extension(fileName);

    /// <summary>Все файлы проекта: по этому префиксу объекты уходят, когда удаляют проект целиком.</summary>
    public static string BoardPrefix(Guid boardId) => $"attachments/{boardId}/";

    /// <summary>
    /// Все файлы задачи. Каскад по префиксу, а не по списку ключей: заодно уносит мусор от загрузок,
    /// у которых объект доехал, а транзакция не прошла.
    /// </summary>
    public static string TaskPrefix(Guid boardId, Guid taskId) => $"{BoardPrefix(boardId)}{taskId}/";

    /// <summary>Расширение из имени: не длиннее 16 символов и только буквы с цифрами, иначе ключ не получает его вовсе.</summary>
    private static string Extension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        if (dot < 0 || dot == fileName.Length - 1)
            return string.Empty;

        var extension = fileName[(dot + 1)..];
        return extension.Length <= 16 && extension.All(char.IsLetterOrDigit)
            ? "." + extension.ToLowerInvariant()
            : string.Empty;
    }

    /// <summary>
    /// Имя приходит от клиента: у браузеров это просто имя, но из скриптов и мобильных клиентов
    /// приезжают пути и управляющие символы. Оставляем последний сегмент и печатные символы.
    /// </summary>
    private static string Sanitize(string? fileName)
    {
        var value = fileName ?? string.Empty;

        var separator = value.LastIndexOfAny(['/', '\\']);
        if (separator >= 0)
            value = value[(separator + 1)..];

        var cleaned = new string(value.Where(symbol => !char.IsControl(symbol)).ToArray()).Trim();

        return Trim(cleaned, FileNameMaxLength);
    }

    private static string Trim(string? value, int maxLength)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
