using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardListQuery;

internal sealed class BoardListQueryHandler(IBoardRepository boards)
    : IRequestHandler<BoardListQuery, IReadOnlyList<BoardResponse>>
{
    public async Task<IReadOnlyList<BoardResponse>> Handle(BoardListQuery request, CancellationToken cancellationToken)
    {
        var all = await boards.GetAllAsync(cancellationToken);
        return all.Select(b => b.ToResponse()).ToList();
    }
}
