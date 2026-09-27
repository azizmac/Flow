using Flow.Application.Security;
using Flow.Application.Abstractions;
using Flow.Application.Features.Attachments;
using MediatR;

namespace Flow.Application.Features.Attachments.Queries.AttachmentMetaQuery;

internal sealed class AttachmentMetaQueryHandler(
    IAttachmentRepository attachments,
    AttachmentOptions options,
    ActorResolver actors,
    IProjectAccess projectAccess)
    : IRequestHandler<AttachmentMetaQuery, AttachmentMeta?>
{
    public async Task<AttachmentMeta?> Handle(AttachmentMetaQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        // Вложение скрытого проекта — 404: прямая ссылка /files/{id} не должна отдавать файл не участнику.
        var attachment = await attachments.GetByIdAsync(request.AttachmentId, cancellationToken);
        if (attachment is null || !(await projectAccess.GetAsync(actor, attachment.BoardId, cancellationToken)).CanView)
            return null;

        var canInline = options.InlineContentTypes.Contains(attachment.ContentType, StringComparer.OrdinalIgnoreCase);

        return new AttachmentMeta(
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.ContentHash,
            canInline);
    }
}
