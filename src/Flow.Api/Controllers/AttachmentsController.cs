using Flow.Application.Abstractions;
using Flow.Application.Features.Attachments.Commands.AttachmentDeleteCommand;
using Flow.Application.Features.Attachments.Commands.AttachmentUploadCommand;
using Flow.Application.Features.Attachments.Queries.AttachmentContentQuery;
using Flow.Application.Features.Attachments.Queries.AttachmentListQuery;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Вложения задачи (docs/TZ_attachments.md). Публичных ссылок нет: и список, и содержимое отдаются
/// только по авторизованному запросу.
/// </summary>
[ApiController]
public class AttachmentsController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    /// <summary>
    /// Потолок тела запроса. Он выше Attachments:MaxFileBytes намеренно: настройка может подрасти
    /// без пересборки, а этот предел отсекает заведомо абсурдные загрузки до чтения тела.
    /// Поднимать MaxFileBytes выше него нельзя — файл отвергнется здесь, не дойдя до понятной ошибки.
    /// </summary>
    private const long RequestCeilingBytes = 64L * 1024 * 1024;

    [HttpGet("tasks/{taskId:guid}/attachments")]
    public async Task<IActionResult> GetAttachments(Guid taskId, CancellationToken cancellationToken)
    {
        var attachments = await mediator.Send(new AttachmentListQuery(taskId), cancellationToken);
        return attachments is null ? NotFound() : Ok(attachments);
    }

    /// <summary>
    /// Загрузка файла: <c>multipart/form-data</c>, поле <c>file</c>. 400 — размер, тип или лимиты
    /// задачи; 409 — такой файл уже приложен; 403 — Reader.
    /// </summary>
    [HttpPost("tasks/{taskId:guid}/attachments")]
    [RequestSizeLimit(RequestCeilingBytes)]
    public async Task<IActionResult> Upload(Guid taskId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { Message = "Файл не передан." });

        // OpenReadStream даёт перематываемый поток (в памяти или во временном файле) — хендлеру
        // нужно прочитать его дважды: сначала хеш и сигнатура, потом отправка в хранилище.
        await using var content = file.OpenReadStream();

        var result = await mediator.Send(
            new AttachmentUploadCommand(actor.Require(), taskId, file.FileName, file.Length, content),
            cancellationToken);

        if (result.IsNotFound)
            return NotFound();

        if (result.IsDuplicate)
            return Conflict(new { Message = result.Error, AttachmentId = result.DuplicateId });

        if (result.IsInvalid)
            return BadRequest(new { Message = result.Error });

        var response = result.Response!;
        return CreatedAtAction(nameof(GetContent), new { id = response.Id }, response);
    }

    /// <summary>
    /// Содержимое файла для клиентов API (Bearer). Страницы Flow сюда не ходят — у них прямые
    /// ссылки /files/{id} (FilesController), которые не требуют заголовка и не грузят circuit.
    ///
    /// По умолчанию всегда скачивание; inline работает только для типов из белого списка (картинки).
    /// Осторожно: параметр объявлен как bool, поэтому «inline=1» НЕ биндится и даёт 400 —
    /// работает только «inline=true».
    /// </summary>
    [HttpGet("attachments/{id:guid}/content")]
    public async Task<IActionResult> GetContent(Guid id, CancellationToken cancellationToken, [FromQuery] bool inline = false)
    {
        var content = await mediator.Send(new AttachmentContentQuery(id), cancellationToken);
        if (content is null)
            return NotFound();

        // Заголовки и отдача — общие с /files/{id}: правила inline и nosniff обязаны жить в одном
        // месте, иначе два входа разойдутся и прямая ссылка на .html станет XSS на своём origin.
        // ETag здесь не ставим: клиенту API он не нужен, а ради него пришлось бы читать строку дважды.
        AttachmentDelivery.ApplyHeaders(this, content.FileName, content.CanInline, forceDownload: !inline, etag: null);
        return AttachmentDelivery.Send(this, content);
    }

    [HttpDelete("attachments/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await mediator.Send(new AttachmentDeleteCommand(actor.Require(), id), cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
