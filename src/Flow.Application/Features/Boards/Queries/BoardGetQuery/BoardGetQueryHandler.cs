using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardGetQuery;

internal sealed class BoardGetQueryHandler(IBoardRepository boards) : IRequestHandler<BoardGetQuery, BoardResponse?>
{
    public async Task<BoardResponse?> Handle(BoardGetQuery request, CancellationToken cancellationToken)
    {
        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        return board?.ToResponse();
    }
}
