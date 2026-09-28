using Flow.Application.Security;
using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskListQuery;

internal sealed class TaskListQueryHandler(ITaskItemRepository tasks, TaskResponses responses, ActorResolver actors, IProjectAccess projectAccess)
    : IRequestHandler<TaskListQuery, IReadOnlyList<TaskResponse>>
{
    public async Task<IReadOnlyList<TaskResponse>> Handle(TaskListQuery request, CancellationToken cancellationToken)
    {
        // Скрытый проект — пустой список, как и несуществующий (у этого маршрута 404 не было никогда).
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        if (!(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken)).CanView)
            return [];

        var items = await tasks.GetByBoardIdAsync(request.BoardId, request.AssigneeId, cancellationToken);

        return await responses.BuildAsync(items, cancellationToken);
    }
}
