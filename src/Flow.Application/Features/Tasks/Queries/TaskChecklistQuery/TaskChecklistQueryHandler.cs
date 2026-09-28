using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskChecklistQuery;

internal sealed class TaskChecklistQueryHandler(ITaskItemRepository tasks, ActorResolver actors, IProjectAccess projectAccess)
    : IRequestHandler<TaskChecklistQuery, IReadOnlyList<TaskChecklistItemResponse>?>
{
    public async Task<IReadOnlyList<TaskChecklistItemResponse>?> Handle(TaskChecklistQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null || !(await projectAccess.GetAsync(actor, task.BoardId, cancellationToken)).CanView)
            return null;

        return task.ChecklistResponse();
    }
}
