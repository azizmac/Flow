using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Sprints;
using MediatR;

namespace Flow.Application.Features.Sprints.Commands.SprintCompleteCommand;

internal sealed class SprintCompleteCommandHandler(
    ISprintRepository sprints,
    IBoardRepository boards,
    ITaskItemRepository tasks,
    ITaskActivityRepository activities,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SprintCompleteCommand, SprintResponse?>
{
    public async Task<SprintResponse?> Handle(SprintCompleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var sprint = await sprints.GetByIdAsync(request.SprintId, cancellationToken);
        if (sprint is null)
            return null;

        permissions.EnsureCanManageSprints(await projectAccess.GetAsync(actor, sprint.BoardId, cancellationToken));

        Sprint? target = null;
        if (request.MoveOpenTo is { } targetId)
            target = await sprints.GetByIdAsync(targetId, cancellationToken)
                     ?? throw new InvalidOperationException($"Sprint {targetId} is not found.");

        var board = await boards.GetByIdAsync(sprint.BoardId, cancellationToken)
                    ?? throw new InvalidOperationException("The sprint's project is not found.");
        var finals = board.Statuses.Where(s => s.IsFinal).Select(s => s.Id).ToHashSet();
        var open = (await tasks.GetBySprintIdAsync(sprint.Id, cancellationToken)).Where(t => !finals.Contains(t.StatusId));

        foreach (var task in sprint.Complete(open, target, DateTime.UtcNow))
            activities.Add(TaskActivity.SprintChanged(task.Id, actor.Id, sprint.Id, target?.Id));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return sprint.ToResponse();
    }
}
