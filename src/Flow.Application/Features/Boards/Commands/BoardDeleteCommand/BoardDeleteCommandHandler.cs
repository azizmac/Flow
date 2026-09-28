using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardDeleteCommand;

internal sealed class BoardDeleteCommandHandler(IBoardRepository boards, IFileStorage storage, IRepositoryWorkspaceService workspaces, ISearchIndexQueue searchIndex, ActorResolver actors, IPermissionService permissions, IProjectAccess projectAccess, IUnitOfWork unitOfWork)
    : IRequestHandler<BoardDeleteCommand, bool>
{
    public async Task<bool> Handle(BoardDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var access = await projectAccess.GetAsync(actor, request.BoardId, cancellationToken);
        permissions.EnsureCanDeleteBoard(actor, access);

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return false;

        await boards.RemoveAsync(board, cancellationToken);

        // Одной записи хватает на весь проект: удаляя чанки доски, воркер убирает и чанки её задач
        // и комментариев — они помнят свой проект в BoardId.
        searchIndex.Enqueue(SearchSourceType.Board, board.Id, board.Id, SearchIndexOperation.Delete);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            // Файлы всех задач проекта лежат под одним префиксом — считать их незачем: удаление проекта
            // редкое, а один лишний листинг дешевле лишнего запроса в базу на каждом удалении.
            await storage.DeleteByPrefixAsync(Attachment.BoardPrefix(board.Id), cancellationToken);
        }
        catch
        {
            // Как и при удалении задачи: строк больше нет, объекты останутся мусором в бакете.
        }

        return true;
    }

        try
        {
            await workspaces.DeleteBoardAsync(board.Id, cancellationToken);
        }
        catch
        {
            // Checkout удаляется после коммита; сбой очистки не отменяет удаление проекта.
        }

}
