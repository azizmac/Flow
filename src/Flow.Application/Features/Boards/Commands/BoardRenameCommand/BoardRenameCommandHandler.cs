using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Application.Features.Boards;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardRenameCommand;

/// <summary>Бросает ArgumentException при пустом названии (см. Board.Rename).</summary>
internal sealed class BoardRenameCommandHandler(IBoardRepository boards, ITaskItemRepository tasks, ISearchIndexQueue searchIndex, ActorResolver actors, IPermissionService permissions, IUnitOfWork unitOfWork)
    : IRequestHandler<BoardRenameCommand, BoardResponse?>
{
    public async Task<BoardResponse?> Handle(BoardRenameCommand request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanManageBoards(await actors.ResolveAsync(request.ActorId, cancellationToken));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        board.Rename(request.Name);

        // Имя проекта входит в шапку чанков его задач и комментариев, но переиндексировать их
        // из-за переименования не нужно: контекст «какого проекта задача» вектор и так знает по коду.
        searchIndex.Enqueue(SearchSourceType.Board, board.Id, board.Id, SearchIndexOperation.Upsert);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var counts = await tasks.CountByBoardIdsAsync([board.Id], cancellationToken);
        return board.ToResponse(counts.GetValueOrDefault(board.Id));
    }
}
