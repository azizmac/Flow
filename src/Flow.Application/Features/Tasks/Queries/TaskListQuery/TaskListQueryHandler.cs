using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskListQuery;

internal sealed class TaskListQueryHandler(ITaskItemRepository tasks, ITaskCommentRepository comments)
    : IRequestHandler<TaskListQuery, IReadOnlyList<TaskResponse>>
{
    public async Task<IReadOnlyList<TaskResponse>> Handle(TaskListQuery request, CancellationToken cancellationToken)
    {
        var items = await tasks.GetByBoardIdAsync(request.BoardId, request.AssigneeId, cancellationToken);

        // Один GROUP BY на весь список, не N+1 (как TaskCount у досок).
        var counts = await comments.CountByTaskIdsAsync(items.Select(t => t.Id).ToList(), cancellationToken);
        return items.Select(t => t.ToResponse(counts.GetValueOrDefault(t.Id))).ToList();
    }
}
