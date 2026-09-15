using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Attachments;
using MediatR;

namespace Flow.Application.Features.Attachments.Queries.AttachmentListQuery;

internal sealed class AttachmentListQueryHandler(
    ITaskItemRepository tasks,
    IAttachmentRepository attachments,
    AttachmentOptions options)
    : IRequestHandler<AttachmentListQuery, IReadOnlyList<AttachmentResponse>?>
{
    public async Task<IReadOnlyList<AttachmentResponse>?> Handle(AttachmentListQuery request, CancellationToken cancellationToken)
    {
        if (await tasks.GetByIdAsync(request.TaskId, cancellationToken) is null)
            return null;

        var found = await attachments.GetByTaskIdAsync(request.TaskId, cancellationToken);
        return found.Select(attachment => attachment.ToResponse(options)).ToArray();
    }
}
