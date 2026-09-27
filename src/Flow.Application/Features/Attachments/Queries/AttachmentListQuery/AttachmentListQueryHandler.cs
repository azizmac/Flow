using Flow.Application.Security;
using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Attachments;
using MediatR;

namespace Flow.Application.Features.Attachments.Queries.AttachmentListQuery;

internal sealed class AttachmentListQueryHandler(
    ITaskItemRepository tasks,
    IAttachmentRepository attachments,
    AttachmentOptions options,
    ActorResolver actors,
    IProjectAccess projectAccess)
    : IRequestHandler<AttachmentListQuery, IReadOnlyList<AttachmentResponse>?>
{
    public async Task<IReadOnlyList<AttachmentResponse>?> Handle(AttachmentListQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null || !(await projectAccess.GetAsync(actor, task.BoardId, cancellationToken)).CanView)
            return null;

        var found = await attachments.GetByTaskIdAsync(request.TaskId, cancellationToken);
        return found.Select(attachment => attachment.ToResponse(options)).ToArray();
    }
}
