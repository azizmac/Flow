using Flow.Application.Abstractions;
using Flow.Application.Security;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardDeleteCommand;

internal sealed class BoardDeleteCommandHandler(IBoardRepository boards, ActorResolver actors, IPermissionService permissions, IUnitOfWork unitOfWork)
    : IRequestHandler<BoardDeleteCommand, bool>
{
    public async Task<bool> Handle(BoardDeleteCommand request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanManageBoards(await actors.ResolveAsync(request.ActorId, cancellationToken));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return false;

        await boards.RemoveAsync(board, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
