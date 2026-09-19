using Flow.Application.Abstractions;
using Flow.Application.Features.Attachments.Commands.AttachmentDeleteCommand;
using Flow.Application.Features.Attachments.Commands.AttachmentUploadCommand;
using Flow.Application.Features.Attachments.Queries.AttachmentContentQuery;
using Flow.Application.Features.Attachments.Queries.AttachmentListQuery;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

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
    /// Содержимое файла. По умолчанию всегда скачивание; <c>inline=1</c> работает только для типов
    /// из белого списка (картинки) — показывать произвольный файл с нашего origin нельзя.
    /// </summary>
    [HttpGet("attachments/{id:guid}/content")]
    public async Task<IActionResult> GetContent(Guid id, CancellationToken cancellationToken, [FromQuery] bool inline = false)
    {
        var content = await mediator.Send(new AttachmentContentQuery(id), cancellationToken);
        if (content is null)
            return NotFound();

        // Даже при inline браузер не должен угадывать тип по содержимому.
        Response.Headers.XContentTypeOptions = "nosniff";

        var disposition = new ContentDispositionHeaderValue(inline && content.CanInline ? "inline" : "attachment");
        // FileNameStar кодирует имя по RFC 5987 — кириллица и пробелы доезжают целыми.
        disposition.SetHttpFileName(content.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();

        // Поток из S3 не seekable (GetObjectResponse.ResponseStream), поэтому MVC сам длину не узнает
        // и ответ уходит chunked: браузер не показывает прогресс скачивания. Длина уже известна из
        // строки вложения — проставляем её руками.
        Response.ContentLength = content.SizeBytes;

        // enableRangeProcessing здесь был бы обманом: диапазоны требуют seekable-потока, MVC на
        // несеекабельном их не отдаёт (ни Accept-Ranges, ни 206). Чтобы перемотка заработала,
        // Range надо прокидывать до S3 отдельной задачей.
        return File(content.Content, content.ContentType);
    }

    [HttpDelete("attachments/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await mediator.Send(new AttachmentDeleteCommand(actor.Require(), id), cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
