using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Search;
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

        // Одной записи хватает на весь проект: удаляя чанки доски, воркер убирает и чанки её задач
        // и комментариев — они помнят свой проект в BoardId.
        searchIndex.Enqueue(SearchSourceType.Board, board.Id, board.Id, SearchIndexOperation.Delete);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
