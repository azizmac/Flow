using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardListQuery;

internal sealed class BoardListQueryHandler(IBoardRepository boards, ITaskItemRepository tasks)
    : IRequestHandler<BoardListQuery, IReadOnlyList<BoardResponse>>
{
    public async Task<IReadOnlyList<BoardResponse>> Handle(BoardListQuery request, CancellationToken cancellationToken)
    {
        var all = await boards.GetAllAsync(cancellationToken);
        if (all.Count == 0)
            return [];

        // Один GROUP BY вместо запроса задач на каждую доску.
        var counts = await tasks.CountByBoardIdsAsync(all.Select(b => b.Id).ToList(), cancellationToken);

        return all
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.ToResponse(counts.GetValueOrDefault(b.Id)))
            .ToList();
    }
}
