namespace Flow.Shared.Contracts.Attachments;

/// <summary>
/// Вложение задачи. Содержимое отдаётся отдельным запросом (GET /attachments/{id}/content):
/// в списке оно не нужно, а для картинок клиент тянет его сам и показывает через blob.
/// </summary>
/// <param name="IsImage">Можно ли показывать файл inline — правило белого списка живёт на сервере.</param>
public sealed record AttachmentResponse(
    Guid Id,
    Guid TaskId,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid UploadedById,
    DateTime UploadedAt,
    bool IsImage);
