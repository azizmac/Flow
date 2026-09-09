using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Application.Features.Boards;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardRenameCommand;

/// <summary>Бросает ArgumentException при пустом названии (см. Board.Rename).</summary>
internal sealed class BoardRenameCommandHandler(IBoardRepository boards, ITaskItemRepository tasks, ActorResolver actors, IPermissionService permissions, IUnitOfWork unitOfWork)
    : IRequestHandler<BoardRenameCommand, BoardResponse?>
{
    public async Task<BoardResponse?> Handle(BoardRenameCommand request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanManageBoards(await actors.ResolveAsync(request.ActorId, cancellationToken));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        board.Rename(request.Name);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var counts = await tasks.CountByBoardIdsAsync([board.Id], cancellationToken);
        return board.ToResponse(counts.GetValueOrDefault(board.Id));
    }
}
