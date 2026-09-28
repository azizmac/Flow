using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardDefaultRoleSetCommand;

internal sealed class BoardDefaultRoleSetCommandHandler(
    IBoardRepository boards,
    IBoardMemberRepository members,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<BoardDefaultRoleSetCommand, BoardMembersResponse?>
{
    public async Task<BoardMembersResponse?> Handle(BoardDefaultRoleSetCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageMembers(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        if (board.DefaultRole != request.Role)
        {
            board.SetDefaultRole(request.Role);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return await members.MembersResponseAsync(board, cancellationToken);
    }
}
