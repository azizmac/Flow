using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Security;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskSetScheduleCommand;

internal sealed class TaskSetScheduleCommandHandler(
    ITaskItemRepository tasks,
    ITaskActivityRepository activities,
    ActorResolver actors,
    IPermissionService permissions,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TaskSetScheduleCommand, TaskUpdateResult>
{
    public async Task<TaskUpdateResult> Handle(TaskSetScheduleCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return TaskUpdateResult.NotFound();

        permissions.EnsureCanEditTask(actor, task);

        var (oldStart, oldDue) = (task.StartDate, task.DueDate);
        if (oldStart == request.StartDate && oldDue == request.DueDate)
            return TaskUpdateResult.Success(task.ToResponse());

        // Проверка «начало ≤ срок» — в домене, до записи в журнал.
        task.SetSchedule(request.StartDate, request.DueDate);

        // По записи на каждое реально изменённое поле, как в TaskUpdate.
        if (oldStart != request.StartDate)
            activities.Add(TaskActivity.StartDateChanged(task.Id, actor.Id, oldStart, request.StartDate));
        if (oldDue != request.DueDate)
            activities.Add(TaskActivity.DueDateChanged(task.Id, actor.Id, oldDue, request.DueDate));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TaskUpdateResult.Success(task.ToResponse());
    }
}
