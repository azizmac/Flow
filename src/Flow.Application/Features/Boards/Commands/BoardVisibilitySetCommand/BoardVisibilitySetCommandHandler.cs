using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardVisibilitySetCommand;

internal sealed class BoardVisibilitySetCommandHandler(
    IBoardRepository boards,
    IBoardMemberRepository members,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<BoardVisibilitySetCommand, BoardMembersResponse?>
{
    public async Task<BoardMembersResponse?> Handle(BoardVisibilitySetCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageMembers(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        if (board.Visibility != request.Visibility)
        {
            board.SetVisibility(request.Visibility);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return board.ToMembersResponse(await members.GetByBoardAsync(board.Id, cancellationToken));
    }
}
