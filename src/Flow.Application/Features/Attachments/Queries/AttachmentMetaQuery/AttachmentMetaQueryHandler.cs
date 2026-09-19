using Flow.Application.Abstractions;
using Flow.Application.Features.Attachments;
using MediatR;

namespace Flow.Application.Features.Attachments.Queries.AttachmentMetaQuery;

internal sealed class AttachmentMetaQueryHandler(
    IAttachmentRepository attachments,
    AttachmentOptions options)
    : IRequestHandler<AttachmentMetaQuery, AttachmentMeta?>
{
    public async Task<AttachmentMeta?> Handle(AttachmentMetaQuery request, CancellationToken cancellationToken)
    {
        var attachment = await attachments.GetByIdAsync(request.AttachmentId, cancellationToken);
        if (attachment is null)
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
