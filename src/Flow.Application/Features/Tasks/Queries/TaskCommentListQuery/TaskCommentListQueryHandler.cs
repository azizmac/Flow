using Flow.Application.Security;
using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskCommentListQuery;

internal sealed class TaskCommentListQueryHandler(ITaskItemRepository tasks, ITaskCommentRepository comments, ActorResolver actors, IProjectAccess projectAccess)
    : IRequestHandler<TaskCommentListQuery, IReadOnlyList<TaskCommentResponse>?>
{
    public async Task<IReadOnlyList<TaskCommentResponse>?> Handle(TaskCommentListQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null || !(await projectAccess.GetAsync(actor, task.BoardId, cancellationToken)).CanView)
            return null;

        var items = await comments.GetByTaskIdAsync(task.Id, cancellationToken);
        return items.Select(c => c.ToResponse()).ToList();
    }
}
