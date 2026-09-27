using Flow.Application.Security;
using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskListQuery;

internal sealed class TaskListQueryHandler(ITaskItemRepository tasks, ITaskCommentRepository comments, ActorResolver actors, IProjectAccess projectAccess)
    : IRequestHandler<TaskListQuery, IReadOnlyList<TaskResponse>>
{
    public async Task<IReadOnlyList<TaskResponse>> Handle(TaskListQuery request, CancellationToken cancellationToken)
    {
        // Скрытый проект — пустой список, как и несуществующий (у этого маршрута 404 не было никогда).
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        if (!(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken)).CanView)
            return [];

        var items = await tasks.GetByBoardIdAsync(request.BoardId, request.AssigneeId, cancellationToken);

        // Один GROUP BY на весь список, не N+1 (как TaskCount у досок).
        var counts = await comments.CountByTaskIdsAsync(items.Select(t => t.Id).ToList(), cancellationToken);
        return items.Select(t => t.ToResponse(counts.GetValueOrDefault(t.Id))).ToList();
    }
}
