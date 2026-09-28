using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Groups;

// Группы людей и роли групп в проектах (docs/TZ_project_access.md §1, §4, этап 4C). Справочник групп, как и людей,
// общий: читают все, меняют глобальные Admin+. Роль группе в проекте выдаёт администратор проекта — не выше своей.
// Итоговая роль человека — максимум из роли по умолчанию, прямого участия и ролей его групп (считает репозиторий).

public sealed record GroupListQuery(Guid ActorId) : IRequest<IReadOnlyList<GroupResponse>>;

public sealed record GroupCreateCommand(Guid ActorId, string Name, string? Description) : IRequest<GroupResponse>;

/// <summary>null — группы нет.</summary>
public sealed record GroupUpdateCommand(Guid ActorId, Guid GroupId, string Name, string? Description) : IRequest<GroupResponse?>;

/// <summary>Удаление уносит состав и роли группы во всех проектах (каскад). false — группы нет.</summary>
public sealed record GroupDeleteCommand(Guid ActorId, Guid GroupId) : IRequest<bool>;

/// <summary>Добавить или убрать человека. Добавляют только активных; повтор — no-op. null — группы нет.</summary>
public sealed record GroupMemberSetCommand(Guid ActorId, Guid GroupId, Guid UserId, bool Member) : IRequest<GroupResponse?>;

/// <summary>Роль группы в проекте (выдать или сменить) — ManageMembers, не выше своей.</summary>
public sealed record BoardGroupSetCommand(Guid ActorId, Guid BoardId, Guid GroupId, ProjectRole Role) : IRequest<BoardMemberResult>;

public sealed record BoardGroupRemoveCommand(Guid ActorId, Guid BoardId, Guid GroupId) : IRequest<BoardMemberResult>;

internal sealed class GroupHandlers(
    IGroupRepository groups,
    IUserRepository users,
    IBoardRepository boards,
    IBoardMemberRepository members,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) :
    IRequestHandler<GroupListQuery, IReadOnlyList<GroupResponse>>,
    IRequestHandler<GroupCreateCommand, GroupResponse>,
    IRequestHandler<GroupUpdateCommand, GroupResponse?>,
    IRequestHandler<GroupDeleteCommand, bool>,
    IRequestHandler<GroupMemberSetCommand, GroupResponse?>,
    IRequestHandler<BoardGroupSetCommand, BoardMemberResult>,
    IRequestHandler<BoardGroupRemoveCommand, BoardMemberResult>
{
    public async Task<IReadOnlyList<GroupResponse>> Handle(GroupListQuery request, CancellationToken cancellationToken)
    {
        await actors.ResolveAsync(request.ActorId, cancellationToken);
        var all = await groups.GetAllAsync(cancellationToken);
        var memberIds = await groups.GetMemberIdsAsync(all.Select(g => g.Id).ToList(), cancellationToken);
        return all.Select(g => ToResponse(g, memberIds.GetValueOrDefault(g.Id) ?? [])).ToList();
    }

    public async Task<GroupResponse> Handle(GroupCreateCommand request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanManageGroups(await actors.ResolveAsync(request.ActorId, cancellationToken));
        var group = Group.Create(request.Name, request.Description);
        await EnsureNameFreeAsync(group.Name, null, cancellationToken);
        groups.Add(group);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(group, []);
    }

    public async Task<GroupResponse?> Handle(GroupUpdateCommand request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanManageGroups(await actors.ResolveAsync(request.ActorId, cancellationToken));
        var group = await groups.GetByIdAsync(request.GroupId, cancellationToken);
        if (group is null)
            return null;

        group.Update(request.Name, request.Description);
        await EnsureNameFreeAsync(group.Name, group.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ResponseAsync(group, cancellationToken);
    }

    public async Task<bool> Handle(GroupDeleteCommand request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanManageGroups(await actors.ResolveAsync(request.ActorId, cancellationToken));
        var group = await groups.GetByIdAsync(request.GroupId, cancellationToken);
        if (group is null)
            return false;

        groups.Remove(group);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<GroupResponse?> Handle(GroupMemberSetCommand request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanManageGroups(await actors.ResolveAsync(request.ActorId, cancellationToken));
        var group = await groups.GetByIdAsync(request.GroupId, cancellationToken);
        if (group is null)
            return null;

        var existing = await groups.GetMemberAsync(group.Id, request.UserId, cancellationToken);
        if (request.Member && existing is null)
        {
            var user = await users.GetByIdAsync(request.UserId, cancellationToken);
            if (user is null || !user.IsActive)
                throw new InvalidOperationException("Человек не найден или деактивирован.");
            groups.AddMember(GroupMember.Create(group.Id, user.Id));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        else if (!request.Member && existing is not null)
        {
            groups.RemoveMember(existing);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return await ResponseAsync(group, cancellationToken);
    }

    public async Task<BoardMemberResult> Handle(BoardGroupSetCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageMembers(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken), request.Role);
        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return BoardMemberResult.NotFound();
        if (await groups.GetByIdAsync(request.GroupId, cancellationToken) is null)
            return BoardMemberResult.Invalid("Группа не найдена.");

        var link = await members.GetGroupAsync(board.Id, request.GroupId, cancellationToken);
        if (link is null)
        {
            members.AddGroup(BoardGroup.Create(board.Id, request.GroupId, request.Role, actor.Id));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        else if (link.ChangeRole(request.Role))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return BoardMemberResult.Success(await members.MembersResponseAsync(board, cancellationToken));
    }

    public async Task<BoardMemberResult> Handle(BoardGroupRemoveCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageMembers(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));
        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return BoardMemberResult.NotFound();

        var link = await members.GetGroupAsync(board.Id, request.GroupId, cancellationToken);
        if (link is null)
            return BoardMemberResult.NotFound();

        members.RemoveGroup(link);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return BoardMemberResult.Success(await members.MembersResponseAsync(board, cancellationToken));
    }

    private async Task<GroupResponse> ResponseAsync(Group group, CancellationToken cancellationToken) =>
        ToResponse(group, (await groups.GetMemberIdsAsync([group.Id], cancellationToken)).GetValueOrDefault(group.Id) ?? []);

    private static GroupResponse ToResponse(Group group, IReadOnlyList<Guid> memberIds) =>
        new(group.Id, group.Name, group.Description, memberIds, group.CreatedAt);

    private async Task EnsureNameFreeAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        if ((await groups.GetAllAsync(cancellationToken)).Any(g => g.Id != exceptId && string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Группа «{name}» уже есть.");
    }
}
