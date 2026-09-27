using Flow.Application.Security;
using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardGetQuery;

internal sealed class BoardGetQueryHandler(IBoardRepository boards, ITaskItemRepository tasks, ActorResolver actors, IProjectAccess projectAccess)
    : IRequestHandler<BoardGetQuery, BoardResponse?>
{
    public async Task<BoardResponse?> Handle(BoardGetQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        // Скрытый проект — как несуществующий: 404, а не 403.
        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null || !(await projectAccess.GetAsync(actor, board.Id, cancellationToken)).CanView)
            return null;

        var counts = await tasks.CountByBoardIdsAsync([board.Id], cancellationToken);
        return board.ToResponse(counts.GetValueOrDefault(board.Id));
    }
}
