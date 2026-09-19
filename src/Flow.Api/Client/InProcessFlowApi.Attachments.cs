using Flow.Application.Features.Attachments.Commands.AttachmentDeleteCommand;
using Flow.Application.Features.Attachments.Commands.AttachmentUploadCommand;
using Flow.Application.Features.Attachments.Queries.AttachmentListQuery;
using Flow.Client.Services;
using Flow.Shared.Contracts.Attachments;
using Microsoft.AspNetCore.Components.Forms;

namespace Flow.Api.Client;

/// <summary>
/// Вложения (AttachmentsController, docs/TZ_attachments.md). Публичных ссылок у файлов нет, поэтому
/// и превью, и скачивание идут через те же вызовы, что и список.
/// </summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<IReadOnlyList<AttachmentResponse>>> GetAttachments(Guid taskId, CancellationToken ct = default) =>
        Scoped<IReadOnlyList<AttachmentResponse>>(async mediator =>
        {
            var attachments = await mediator.Send(new AttachmentListQuery(taskId), ct);
            return attachments is null ? NotFound<IReadOnlyList<AttachmentResponse>>() : Ok(attachments);
        });

    /// <summary>
    /// Загрузка файла. multipart больше нет — команда получает поток напрямую, но остальные исходы
    /// те же: 409 — такой файл уже приложен, 400 — размер, тип и лимиты задачи, 403 — Reader.
    /// </summary>
    public Task<ApiResult<AttachmentResponse>> UploadAttachment(Guid taskId, IBrowserFile file, long maxBytes, CancellationToken ct = default) =>
        Scoped<AttachmentResponse>(async mediator =>
        {
            // Контроллер отсекал пустое тело до обращения к хендлеру — здесь ту же роль играет Size.
            if (file is null || file.Size == 0)
                return Invalid<AttachmentResponse>("Файл не передан.");

            // Потолок проверяем здесь, а не на обрыве чтения: буфер ниже выделяется сразу на весь
            // размер, и файл в гигабайт съел бы гигабайт памяти до первой ошибки. OutOfMemory Guard
            // не ловит — упал бы весь circuit вместо понятного тоста.
            if (file.Size > maxBytes)
                return Invalid<AttachmentResponse>("Файл больше допустимого размера.");

            var actor = await ActorAsync();

            // Хендлер читает поток дважды (сначала хеш и сигнатура, потом отправка в хранилище) и
            // перематывает его на 0. IFormFile такой давал, а OpenReadStream в circuit'е — нет:
            // это односторонний поток поверх SignalR. Отсюда буфер в памяти; его потолок задаёт
            // maxBytes, который вызывающий берёт из Attachments:MaxFileBytes.
            using var content = new MemoryStream(file.Size <= int.MaxValue ? (int)file.Size : 0);

            try
            {
                await using var source = file.OpenReadStream(maxBytes, ct);
                await source.CopyToAsync(content, ct);
            }
            catch (IOException)
            {
                // Поток поверх SignalR оборвался (в том числе на собственной проверке maxBytes) —
                // ровно тот же случай, который HTTP-клиент ловил на своей стороне, с тем же текстом.
                return Invalid<AttachmentResponse>("Файл больше допустимого размера.");
            }

            content.Position = 0;

            var result = await mediator.Send(
                new AttachmentUploadCommand(actor, taskId, file.Name, file.Size, content),
                ct);

            if (result.IsNotFound)
                return NotFound<AttachmentResponse>();

            // DuplicateId контроллер клал рядом с сообщением, но HTTP-клиент доставал из тела только
            // message — экраны видели один текст, и менять это вместе с транспортом не надо.
            if (result.IsDuplicate)
                return Conflict<AttachmentResponse>(result.Error!);

            if (result.IsInvalid)
                return Invalid<AttachmentResponse>(result.Error!);

            return Ok(result.Response!);
        });

    public Task<ApiResult<bool>> DeleteAttachment(Guid id, CancellationToken ct = default) =>
        Scoped<bool>(async mediator =>
        {
            var actor = await ActorAsync();
            var deleted = await mediator.Send(new AttachmentDeleteCommand(actor, id), ct);
            // 404 у Delete был штатным ответом, а не ошибкой: экран просто считает, что удалять нечего.
            return deleted ? Ok(true) : Missing();
        });
}
