using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using Flow.Application.Features.Tasks;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Groups;

// Группы людей и роли групп в проектах (docs/TZ_project_access.md §1, §4, этап 4C). Справочник групп, как и людей,
// общий: читают все, меняют глобальные Admin+. Роль группе в проекте выдаёт администратор проекта — не выше своей.
// Итоговая роль человека — максимум из роли по умолчанию, прямого участия и ролей его групп (считает репозиторий).

public sealed record GroupListQuery(Guid ActorId) : IRequest<IReadOnlyList<GroupResponse>>;

public sealed record GroupCreateCommand(Guid ActorId, string Name, string? Description, bool IsTeam = false) : IRequest<GroupResponse>;

/// <summary>null — группы нет.</summary>
public sealed record GroupUpdateCommand(Guid ActorId, Guid GroupId, string Name, string? Description, bool? IsTeam = null) : IRequest<GroupResponse?>;

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
        var group = Group.Create(request.Name, request.Description, request.IsTeam);
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
        if (request.IsTeam is { } isTeam)
            group.SetTeam(isTeam);
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
        new(group.Id, group.Name, group.Description, memberIds, group.CreatedAt, group.IsTeam);

    private async Task EnsureNameFreeAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        if ((await groups.GetAllAsync(cancellationToken)).Any(g => g.Id != exceptId && string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Группа «{name}» уже есть.");
    }
}

/// <summary>Команда задачи (этап 4D): группа с IsTeam или null — снять. Права — правка задачи; журнал TeamChanged.</summary>
public sealed record TaskSetTeamCommand(Guid ActorId, Guid TaskId, Guid? TeamId) : IRequest<TaskUpdateResult>;

internal sealed class TaskSetTeamCommandHandler(
    ITaskItemRepository tasks,
    IGroupRepository groups,
    ITaskActivityRepository activities,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) : IRequestHandler<TaskSetTeamCommand, TaskUpdateResult>
{
    public async Task<TaskUpdateResult> Handle(TaskSetTeamCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return TaskUpdateResult.NotFound();

        permissions.EnsureCanEditTask(actor, await projectAccess.GetAsync(actor, task.BoardId, cancellationToken), task);

        // Снятый флаг «команда» не выбивает её из старых задач, но новой задаче такую группу не назначить.
        if (request.TeamId is { } teamId && request.TeamId != task.TeamId
            && await groups.GetByIdAsync(teamId, cancellationToken) is not { IsTeam: true })
            throw new InvalidOperationException("Такой команды нет.");

        var old = task.TeamId;
        if (task.SetTeam(request.TeamId))
        {
            activities.Add(TaskActivity.TeamChanged(task.Id, actor.Id, old, request.TeamId));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return TaskUpdateResult.Success(task.ToResponse());
    }
}
