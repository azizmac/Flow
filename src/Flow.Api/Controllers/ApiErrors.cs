namespace Flow.Api.Controllers;

/// <summary>
/// Тело ошибок JSON-API: <c>{ "message": "…" }</c> — 400 (ввод и бизнес-правила), 401/403 (ApiExceptionFilter), 409.
/// Отдельный тип, а не анонимный объект, чтобы Swagger видел схему ([ProducesResponseType]) и она не могла
/// разойтись с тем, что контроллеры отдают на самом деле. 404 без тела — [ApiController] превращает его в ProblemDetails.
/// </summary>
public sealed record ApiError(string Message);

/// <summary>409 на загрузке: такой файл уже приложен к задаче, <c>AttachmentId</c> — уже существующее вложение.</summary>
public sealed record DuplicateAttachmentError(string Message, Guid? AttachmentId);

/// <summary>202 на переиндексации: сколько источников поставлено в очередь.</summary>
public sealed record ReindexAccepted(int Enqueued);
