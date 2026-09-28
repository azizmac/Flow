using Flow.Application.Features.PermissionSets;
using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;

internal sealed class BoardMemberSetCommandHandler(
    IBoardRepository boards,
    IBoardMemberRepository members,
    IUserRepository users,
    IPermissionSetRepository permissionSets,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<BoardMemberSetCommand, BoardMemberResult>
{
    public async Task<BoardMemberResult> Handle(BoardMemberSetCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var (role, setId) = await PermissionSetRoles.RoleOfAsync(permissionSets, request.Role, request.PermissionSetId, cancellationToken);
        permissions.EnsureCanManageMembers(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken), role);

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

            members.Add(BoardMember.Create(board.Id, user.Id, role, actor.Id, setId));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        else
        {
            EnsureNotLastPrivateAdmin(board, await members.GetByBoardAsync(board.Id, cancellationToken), member, role);
            if (!member.ChangeRole(role, setId))
                return BoardMemberResult.Success(await members.MembersResponseAsync(board, cancellationToken));

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return BoardMemberResult.Success(await members.MembersResponseAsync(board, cancellationToken));
    }

    /// <summary>
    /// В приватном проекте нельзя снять или понизить последнего участника-администратора: глобальные Admin/Owner
    /// проект всё равно увидят, но терять единственного локального администратора незачем (docs/TZ_project_access.md §4).
    /// </summary>
    internal static void EnsureNotLastPrivateAdmin(Board board, IReadOnlyList<BoardMember> current, BoardMember member, ProjectRole? newRole)
    {
        if (board.Visibility != BoardVisibility.Private || member.Role != ProjectRole.Admin || newRole == ProjectRole.Admin)
            return;

        if (current.Count(m => m.Role == ProjectRole.Admin) <= 1)
            throw new InvalidOperationException("Это последний администратор приватного проекта: сначала назначьте другого.");
    }
}
