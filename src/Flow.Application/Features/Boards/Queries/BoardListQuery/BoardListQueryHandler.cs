using Flow.Application.Security;
using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardListQuery;

internal sealed class BoardListQueryHandler(IBoardRepository boards, ITaskItemRepository tasks, ActorResolver actors, IProjectAccess projectAccess)
    : IRequestHandler<BoardListQuery, IReadOnlyList<BoardResponse>>
{
    public async Task<IReadOnlyList<BoardResponse>> Handle(BoardListQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var visible = await projectAccess.VisibleBoardIdsAsync(actor, cancellationToken);

        // Проектов немного — их и так грузят все; фильтр по видимым в памяти не стоит второго запроса.
        var all = (await boards.GetAllAsync(cancellationToken))
            .Where(b => visible is null || visible.Contains(b.Id))
            .ToList();
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
