using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Sprints.Commands.SprintDeleteCommand;

internal sealed class SprintDeleteCommandHandler(
    ISprintRepository sprints,
    ITaskItemRepository tasks,
    ITaskActivityRepository activities,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SprintDeleteCommand, bool>
{
    public async Task<bool> Handle(SprintDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var sprint = await sprints.GetByIdAsync(request.SprintId, cancellationToken);
        if (sprint is null)
            return false;

        permissions.EnsureCanManageSprints(await projectAccess.GetAsync(actor, sprint.BoardId, cancellationToken));
        if (sprint.State != SprintState.Planned)
            throw new InvalidOperationException("Only a planned sprint can be deleted.");

        // Задачи — в бэклог явно, с журналом: FK SetNull в БД сделал бы то же молча.
        foreach (var task in await tasks.GetBySprintIdAsync(sprint.Id, cancellationToken))
        {
            task.SetSprint(null);
            activities.Add(TaskActivity.SprintChanged(task.Id, actor.Id, sprint.Id, null));
        }

        sprints.Remove(sprint);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
