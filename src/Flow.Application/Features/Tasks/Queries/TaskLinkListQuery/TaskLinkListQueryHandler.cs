using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskLinkListQuery;

internal sealed class TaskLinkListQueryHandler(ITaskItemRepository tasks, ITaskLinkRepository links, TaskLinkResponses responses, ActorResolver actors, IProjectAccess projectAccess)
    : IRequestHandler<TaskLinkListQuery, IReadOnlyList<TaskLinkResponse>?>
{
    public async Task<IReadOnlyList<TaskLinkResponse>?> Handle(TaskLinkListQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null || !(await projectAccess.GetAsync(actor, task.BoardId, cancellationToken)).CanView)
            return null;

        return await responses.BuildAsync(actor, task.Id, await links.GetByTaskIdAsync(task.Id, cancellationToken), cancellationToken);
    }
}
