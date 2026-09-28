using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Sprints;
using MediatR;

namespace Flow.Application.Features.Sprints.Commands.SprintUpdateCommand;

internal sealed class SprintUpdateCommandHandler(
    ISprintRepository sprints,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SprintUpdateCommand, SprintResponse?>
{
    public async Task<SprintResponse?> Handle(SprintUpdateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var sprint = await sprints.GetByIdAsync(request.SprintId, cancellationToken);
        if (sprint is null)
            return null;

        permissions.EnsureCanManageSprints(await projectAccess.GetAsync(actor, sprint.BoardId, cancellationToken));

        if (request.Name is not null)
            sprint.Rename(request.Name);
        if (request.Goal is not null || request.ClearGoal)
            sprint.SetGoal(request.ClearGoal ? null : request.Goal);
        if (request.ClearDates)
            sprint.SetDates(null, null);
        else if (request.StartDate is not null || request.EndDate is not null)
            sprint.SetDates(request.StartDate ?? sprint.StartDate, request.EndDate ?? sprint.EndDate);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return sprint.ToResponse();
    }
}
