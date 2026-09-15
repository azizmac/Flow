using Flow.Application.Abstractions;
using Flow.Application.Security;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardDeleteCommand;

internal sealed class BoardDeleteCommandHandler(IBoardRepository boards, ISearchIndexQueue searchIndex, ActorResolver actors, IPermissionService permissions, IUnitOfWork unitOfWork)
    : IRequestHandler<BoardDeleteCommand, bool>
{
    public async Task<bool> Handle(BoardDeleteCommand request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanManageBoards(await actors.ResolveAsync(request.ActorId, cancellationToken));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return false;

        await boards.RemoveAsync(board, cancellationToken);

        // Одной записи хватает на весь проект: чанки задач и комментариев помечены тем же BoardId,
        // и воркер сносит их вместе с чанком самого проекта (см. SearchIndexingWorker.DeleteAsync).
        // Задачи перечисляются отдельно, чтобы их чанки исчезли даже из индекса, собранного до появления BoardId.
        foreach (var task in board.Tasks)
            searchIndex.Enqueue(SearchSourceType.Task, task.Id, board.Id, SearchIndexOperation.Delete);

        searchIndex.Enqueue(SearchSourceType.Board, board.Id, board.Id, SearchIndexOperation.Delete);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
