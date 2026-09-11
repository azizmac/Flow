using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Application.Features.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;

/// <summary>Бросает ArgumentException при пустом названии (см. TaskItem.Rename).</summary>
internal sealed class TaskUpdateCommandHandler(ITaskItemRepository tasks, ITaskActivityRepository activities, ActorResolver actors, IPermissionService permissions, IUnitOfWork unitOfWork)
    : IRequestHandler<TaskUpdateCommand, TaskUpdateResult>
{
    public async Task<TaskUpdateResult> Handle(TaskUpdateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return TaskUpdateResult.NotFound();

        permissions.EnsureCanEditTask(actor, task);

        if (request.StatusId is not null)
        {
            var statusBelongsToBoard = await tasks.StatusBelongsToBoardAsync(request.StatusId.Value, task.BoardId, cancellationToken);
            if (!statusBelongsToBoard)
                return TaskUpdateResult.InvalidStatus(request.StatusId.Value);
        }

        // Журнал: по записи на каждое реально изменённое поле; то же значение — без записи.
        if (request.Title is not null)
        {
            var oldTitle = task.Title;
            task.Rename(request.Title);
            if (task.Title != oldTitle)
                activities.Add(TaskActivity.TitleChanged(task.Id, actor.Id, oldTitle, task.Title));
        }

        if (request.Description is not null && request.Description != (task.Description ?? string.Empty))
        {
            task.UpdateDescription(request.Description);
            activities.Add(TaskActivity.DescriptionChanged(task.Id, actor.Id));
        }

        if (request.StatusId is not null && request.StatusId.Value != task.StatusId)
        {
            activities.Add(TaskActivity.StatusChanged(task.Id, actor.Id, task.StatusId, request.StatusId.Value));
            task.ChangeStatus(request.StatusId.Value);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TaskUpdateResult.Success(task.ToResponse());
    }
}
