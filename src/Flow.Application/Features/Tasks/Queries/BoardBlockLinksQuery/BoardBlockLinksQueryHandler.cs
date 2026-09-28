using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.BoardBlockLinksQuery;

internal sealed class BoardBlockLinksQueryHandler(ITaskLinkRepository links, IBoardRepository boards, ActorResolver actors, IProjectAccess projectAccess)
    : IRequestHandler<BoardBlockLinksQuery, IReadOnlyList<TaskBlockEdge>?>
{
    public async Task<IReadOnlyList<TaskBlockEdge>?> Handle(BoardBlockLinksQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        if (!(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken)).CanView
            || await boards.GetByIdAsync(request.BoardId, cancellationToken) is null)
            return null;

        return (await links.GetBlocksWithinBoardAsync(request.BoardId, cancellationToken))
            .Select(l => new TaskBlockEdge(l.SourceId, l.TargetId)).ToList();
    }
}
