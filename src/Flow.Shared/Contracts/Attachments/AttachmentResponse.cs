namespace Flow.Shared.Contracts.Attachments;

/// <summary>
/// Вложение задачи. Содержимое отдаётся отдельным запросом (GET /attachments/{id}/content):
/// в списке оно не нужно, а для картинок клиент тянет его сам и показывает через blob.
/// </summary>
/// <param name="IsImage">Можно ли показывать файл inline — правило белого списка живёт на сервере.</param>
/// <param name="Width">
/// Отображаемая ширина картинки; null — размеры неизвестны (не картинка, битый заголовок или файл,
/// загруженный до появления этого поля). Нужны разметке: по паре width/height браузер резервирует
/// место под картинку ещё до загрузки, иначе loading="lazy" почти не работает.
/// </param>
public sealed record AttachmentResponse(
    Guid Id,
    Guid TaskId,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid UploadedById,
    DateTime UploadedAt,
    bool IsImage,
    int? Width = null,
    int? Height = null);
