using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Security;
using MediatR;

namespace Flow.Application.Features.Sprints.Commands.TaskSetSprintCommand;

internal sealed class TaskSetSprintCommandHandler(
    ITaskItemRepository tasks,
    TaskSprints taskSprints,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TaskSetSprintCommand, TaskUpdateResult>
{
    public async Task<TaskUpdateResult> Handle(TaskSetSprintCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return TaskUpdateResult.NotFound();

        permissions.EnsureCanEditTask(actor, await projectAccess.GetAsync(actor, task.BoardId, cancellationToken), task);

        if (await taskSprints.MoveAsync(task, request.SprintId, actor.Id, cancellationToken))
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return TaskUpdateResult.Success(task.ToResponse());
    }
}
