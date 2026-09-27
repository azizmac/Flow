using Flow.Application.Security;
using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskGetQuery;

internal sealed class TaskGetQueryHandler(ITaskItemRepository tasks, ITaskCommentRepository comments, ActorResolver actors, IProjectAccess projectAccess) : IRequestHandler<TaskGetQuery, TaskResponse?>
{
    public async Task<TaskResponse?> Handle(TaskGetQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null || !(await projectAccess.GetAsync(actor, task.BoardId, cancellationToken)).CanView)
            return null;

        var counts = await comments.CountByTaskIdsAsync([task.Id], cancellationToken);
        var children = await tasks.CountChildrenAsync([task.Id], cancellationToken);
        return task.ToResponse(counts.GetValueOrDefault(task.Id), children.GetValueOrDefault(task.Id));
    }
}
