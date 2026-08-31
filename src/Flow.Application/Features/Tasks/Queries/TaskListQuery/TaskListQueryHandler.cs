using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskListQuery;

internal sealed class TaskListQueryHandler(ITaskItemRepository tasks)
    : IRequestHandler<TaskListQuery, IReadOnlyList<TaskResponse>>
{
    public async Task<IReadOnlyList<TaskResponse>> Handle(TaskListQuery request, CancellationToken cancellationToken)
    {
        var items = await tasks.GetByBoardIdAsync(request.BoardId, cancellationToken);
        return items.Select(t => t.ToResponse()).ToList();
    }
}
