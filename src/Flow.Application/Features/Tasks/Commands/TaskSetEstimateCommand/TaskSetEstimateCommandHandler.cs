using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Security;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskSetEstimateCommand;

internal sealed class TaskSetEstimateCommandHandler(
    ITaskItemRepository tasks,
    ITaskActivityRepository activities,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TaskSetEstimateCommand, TaskUpdateResult>
{
    public async Task<TaskUpdateResult> Handle(TaskSetEstimateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return TaskUpdateResult.NotFound();

        permissions.EnsureCanEditTask(actor, await projectAccess.GetAsync(actor, task.BoardId, cancellationToken), task);

        var (oldPoints, oldMinutes) = (task.StoryPoints, task.EstimateMinutes);

        // Сначала оба значения проверяет домен: неверное второе не должно оставить в журнале запись о первом.
        task.SetStoryPoints(request.StoryPoints);
        task.SetEstimate(request.EstimateMinutes);

        var changed = false;
        if (oldPoints != request.StoryPoints)
        {
            activities.Add(TaskActivity.StoryPointsChanged(task.Id, actor.Id, oldPoints, request.StoryPoints));
            changed = true;
        }

        if (oldMinutes != request.EstimateMinutes)
        {
            activities.Add(TaskActivity.EstimateChanged(task.Id, actor.Id, oldMinutes, request.EstimateMinutes));
            changed = true;
        }

        if (changed)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return TaskUpdateResult.Success(task.ToResponse());
    }
}
