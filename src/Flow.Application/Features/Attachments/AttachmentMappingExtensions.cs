using Flow.Domain.Entities;
using Flow.Shared.Contracts.Attachments;

namespace Flow.Application.Features.Attachments;

public static class AttachmentMappingExtensions
{
    /// <summary>
    /// IsImage считает сервер: правило «что можно показывать inline» — вопрос безопасности,
    /// и клиенту его знать не нужно (см. AttachmentOptions.InlineContentTypes).
    /// </summary>
    public static AttachmentResponse ToResponse(this Attachment attachment, AttachmentOptions options) =>
        new(attachment.Id,
            attachment.TaskId,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.UploadedById,
            attachment.UploadedAt,
            options.InlineContentTypes.Contains(attachment.ContentType, StringComparer.OrdinalIgnoreCase),
            attachment.Width,
            attachment.Height);
}
