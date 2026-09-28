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
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TaskSetDueDateCommand, TaskUpdateResult>
{
    public async Task<TaskUpdateResult> Handle(TaskSetDueDateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return TaskUpdateResult.NotFound();

        permissions.EnsureCanEditTask(actor, await projectAccess.GetAsync(actor, task.BoardId, cancellationToken), task);

        if (task.DueDate != request.DueDate)
        {
            // Сначала домен (срок раньше даты начала — ArgumentException), потом журнал: иначе отказ
            // оставлял бы в журнале запись об изменении, которого не было.
            var oldDueDate = task.DueDate;
            task.SetDueDate(request.DueDate);
            activities.Add(TaskActivity.DueDateChanged(task.Id, actor.Id, oldDueDate, request.DueDate));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return TaskUpdateResult.Success(task.ToResponse());
    }
}
