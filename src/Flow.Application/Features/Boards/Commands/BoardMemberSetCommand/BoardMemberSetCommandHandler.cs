using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;

internal sealed class BoardMemberSetCommandHandler(
    IBoardRepository boards,
    IBoardMemberRepository members,
    IUserRepository users,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<BoardMemberSetCommand, BoardMemberResult>
{
    public async Task<BoardMemberResult> Handle(BoardMemberSetCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageMembers(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken), request.Role);

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return BoardMemberResult.NotFound();

        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return BoardMemberResult.Invalid($"User {request.UserId} not found.");

        var member = await members.GetAsync(board.Id, user.Id, cancellationToken);
        if (member is null)
        {
            // Новых участников — только из работающих: деактивированный в проекте не нужен. Уже добавленный
            // остаётся в списке (история), а его права отсекает ActorResolver.
            if (!user.IsActive)
                return BoardMemberResult.Invalid($"User {user.Username} is deactivated.");

            members.Add(BoardMember.Create(board.Id, user.Id, request.Role, actor.Id));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        else if (member.ChangeRole(request.Role))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return BoardMemberResult.Success(board.ToMembersResponse(await members.GetByBoardAsync(board.Id, cancellationToken)));
    }
}
