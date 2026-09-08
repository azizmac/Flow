using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardGetQuery;

internal sealed class BoardGetQueryHandler(IBoardRepository boards, ITaskItemRepository tasks)
    : IRequestHandler<BoardGetQuery, BoardResponse?>
{
    public async Task<BoardResponse?> Handle(BoardGetQuery request, CancellationToken cancellationToken)
    {
        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        var counts = await tasks.CountByBoardIdsAsync([board.Id], cancellationToken);
        return board.ToResponse(counts.GetValueOrDefault(board.Id));
    }
}
