using Flow.Application.Security;
using Flow.Application.Abstractions;
using Flow.Application.Features.Attachments;
using MediatR;

namespace Flow.Application.Features.Attachments.Queries.AttachmentContentQuery;

internal sealed class AttachmentContentQueryHandler(
    IAttachmentRepository attachments,
    IFileStorage storage,
    AttachmentOptions options,
    ActorResolver actors,
    IProjectAccess projectAccess)
    : IRequestHandler<AttachmentContentQuery, AttachmentContent?>
{
    public async Task<AttachmentContent?> Handle(AttachmentContentQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        // Вложение скрытого проекта — 404: прямая ссылка /files/{id} не должна отдавать файл не участнику.
        var attachment = await attachments.GetByIdAsync(request.AttachmentId, cancellationToken);
        if (attachment is null || !(await projectAccess.GetAsync(actor, attachment.BoardId, cancellationToken)).CanView)
            return null;

        // Объекта может не быть: сбой при загрузке или чистка бакета руками. Для клиента это 404,
        // а не 500 — строка есть, файла нет.
        var content = await storage.OpenReadAsync(attachment.StorageKey, cancellationToken);
        if (content is null)
            return null;

        var canInline = options.InlineContentTypes.Contains(attachment.ContentType, StringComparer.OrdinalIgnoreCase);

        return new AttachmentContent(content, attachment.FileName, attachment.ContentType, attachment.SizeBytes, canInline);
    }
}
