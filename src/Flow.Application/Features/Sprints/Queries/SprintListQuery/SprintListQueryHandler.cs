using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Sprints;
using MediatR;

namespace Flow.Application.Features.Sprints.Queries.SprintListQuery;

internal sealed class SprintListQueryHandler(ISprintRepository sprints, ActorResolver actors, IProjectAccess projectAccess)
    : IRequestHandler<SprintListQuery, IReadOnlyList<SprintResponse>?>
{
    public async Task<IReadOnlyList<SprintResponse>?> Handle(SprintListQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        if (!(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken)).CanView)
            return null;

        return (await sprints.GetByBoardAsync(request.BoardId, includeCompleted: true, cancellationToken)).Select(s => s.ToResponse()).ToList();
    }
}
