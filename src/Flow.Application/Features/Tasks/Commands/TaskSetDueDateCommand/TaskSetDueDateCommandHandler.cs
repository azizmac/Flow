using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Security;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskSetDueDateCommand;

internal sealed class TaskSetDueDateCommandHandler(
    ITaskItemRepository tasks,
    ITaskActivityRepository activities,
    ActorResolver actors,
    IPermissionService permissions,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TaskSetDueDateCommand, TaskUpdateResult>
{
    public async Task<TaskUpdateResult> Handle(TaskSetDueDateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return TaskUpdateResult.NotFound();

        permissions.EnsureCanEditTask(actor, task);

        if (task.DueDate != request.DueDate)
        {
            activities.Add(TaskActivity.DueDateChanged(task.Id, actor.Id, task.DueDate, request.DueDate));
            task.SetDueDate(request.DueDate);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return TaskUpdateResult.Success(task.ToResponse());
    }
}
