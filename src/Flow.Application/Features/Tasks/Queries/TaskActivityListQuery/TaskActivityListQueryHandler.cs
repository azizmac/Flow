using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskActivityListQuery;

internal sealed class TaskActivityListQueryHandler(ITaskItemRepository tasks, ITaskActivityRepository activities)
    : IRequestHandler<TaskActivityListQuery, IReadOnlyList<TaskActivityResponse>?>
{
    public async Task<IReadOnlyList<TaskActivityResponse>?> Handle(TaskActivityListQuery request, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return null;

        var items = await activities.GetByTaskIdAsync(task.Id, cancellationToken);
        return items.Select(a => a.ToResponse()).ToList();
    }
}
