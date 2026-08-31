using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskGetQuery;

internal sealed class TaskGetQueryHandler(ITaskItemRepository tasks) : IRequestHandler<TaskGetQuery, TaskResponse?>
{
    public async Task<TaskResponse?> Handle(TaskGetQuery request, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        return task?.ToResponse();
    }
}
