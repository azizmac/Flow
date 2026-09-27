using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Sprints;
using MediatR;

namespace Flow.Application.Features.Sprints.Commands.SprintCreateCommand;

internal sealed class SprintCreateCommandHandler(
    IBoardRepository boards,
    ISprintRepository sprints,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SprintCreateCommand, SprintResponse?>
{
    public async Task<SprintResponse?> Handle(SprintCreateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageSprints(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));
        if (await boards.GetByIdAsync(request.BoardId, cancellationToken) is null)
            return null;

        var order = await sprints.NextSortOrderAsync(request.BoardId, cancellationToken);
        var name = string.IsNullOrWhiteSpace(request.Name) ? $"Спринт {order + 1}" : request.Name;
        var sprint = Sprint.Create(request.BoardId, name, request.Goal, order, request.StartDate, request.EndDate);
        sprints.Add(sprint);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return sprint.ToResponse();
    }
}
