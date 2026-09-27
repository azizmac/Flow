using Flow.Application.Abstractions;
using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using Flow.Application.Security;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardMemberRemoveCommand;

internal sealed class BoardMemberRemoveCommandHandler(
    IBoardRepository boards,
    IBoardMemberRepository members,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<BoardMemberRemoveCommand, BoardMemberResult>
{
    public async Task<BoardMemberResult> Handle(BoardMemberRemoveCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageMembers(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return BoardMemberResult.NotFound();

        var member = await members.GetAsync(board.Id, request.UserId, cancellationToken);
        if (member is null)
            return BoardMemberResult.NotFound();

        members.Remove(member);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BoardMemberResult.Success(board.ToMembersResponse(await members.GetByBoardAsync(board.Id, cancellationToken)));
    }
}
