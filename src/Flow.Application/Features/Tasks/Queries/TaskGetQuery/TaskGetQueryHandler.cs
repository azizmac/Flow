using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskGetQuery;

internal sealed class TaskGetQueryHandler(ITaskItemRepository tasks, ITaskCommentRepository comments) : IRequestHandler<TaskGetQuery, TaskResponse?>
{
    public async Task<TaskResponse?> Handle(TaskGetQuery request, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return null;

        var counts = await comments.CountByTaskIdsAsync([task.Id], cancellationToken);
        return task.ToResponse(counts.GetValueOrDefault(task.Id));
    }
}
