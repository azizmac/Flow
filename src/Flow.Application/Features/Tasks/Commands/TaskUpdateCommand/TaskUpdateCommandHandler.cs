using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Application.Features.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;

/// <summary>Бросает ArgumentException при пустом названии (см. TaskItem.Rename).</summary>
internal sealed class TaskUpdateCommandHandler(ITaskItemRepository tasks, ActorResolver actors, IPermissionService permissions, IUnitOfWork unitOfWork)
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

        if (request.Title is not null)
            task.Rename(request.Title);

        if (request.Description is not null)
            task.UpdateDescription(request.Description);

        if (request.StatusId is not null)
            task.ChangeStatus(request.StatusId.Value);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TaskUpdateResult.Success(task.ToResponse());
    }
}
