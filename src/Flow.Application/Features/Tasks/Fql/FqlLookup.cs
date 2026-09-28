using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;

namespace Flow.Application.Features.Tasks.Fql;

/// <summary>
/// <see cref="IFqlLookup"/> поверх репозиториев для конкретного actor'а: проекты и задачи — только из видимых
/// проектов (docs/TZ_project_access.md). Экземпляр на один запрос: список видимых проектов считается один раз.
/// </summary>
internal sealed class FqlLookup(
    User actor,
    IBoardRepository boards,
    IUserRepository users,
    ITaskItemRepository tasks,
    ITaskLinkRepository links,
    IProjectAccess projectAccess,
    ISprintRepository sprints,
    IMilestoneRepository milestones,
    IGroupRepository groups) : IFqlLookup
{
    private IReadOnlyList<Board>? _visible;

    public async Task<IReadOnlyList<Board>> VisibleBoardsAsync(CancellationToken cancellationToken)
    {
        if (_visible is not null)
            return _visible;

        var all = await boards.GetAllAsync(cancellationToken);
        var visible = await projectAccess.VisibleBoardIdsAsync(actor, cancellationToken);
        return _visible = visible is null ? all : all.Where(b => visible.Contains(b.Id)).ToList();
    }

    public async Task<IReadOnlyDictionary<string, Guid>> UsersByUsernameAsync(IReadOnlyCollection<string> usernames, CancellationToken cancellationToken) =>
        (await users.GetByUsernamesAsync(usernames, cancellationToken)).ToDictionary(u => u.Username, u => u.Id);

    public async Task<TaskItem?> TaskByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByCodeAsync(code, cancellationToken);
        return task is not null && (await VisibleBoardsAsync(cancellationToken)).Any(b => b.Id == task.BoardId) ? task : null;
    }

    public async Task<IReadOnlyList<Guid>> DescendantsAsync(TaskItem root, CancellationToken cancellationToken) =>
        (await tasks.GetTreeAsync(root.BoardId, root.Id, cancellationToken)).Where(e => e.Depth > 0).Select(e => e.Task.Id).ToList();

    public async Task<IReadOnlyList<Guid>> LinkedAsync(Guid taskId, CancellationToken cancellationToken) =>
        (await links.GetByTaskIdAsync(taskId, cancellationToken)).Select(l => l.OtherTaskId(taskId)).Distinct().ToList();

    public async Task<IReadOnlyList<Sprint>> SprintsAsync(CancellationToken cancellationToken)
    {
        var result = new List<Sprint>();
        foreach (var board in await VisibleBoardsAsync(cancellationToken))
            result.AddRange(await sprints.GetByBoardAsync(board.Id, includeCompleted: true, cancellationToken));
        return result;
    }

    public async Task<IReadOnlyList<Milestone>> MilestonesAsync(CancellationToken cancellationToken)
    {
        var result = new List<Milestone>();
        foreach (var board in await VisibleBoardsAsync(cancellationToken))
            result.AddRange(await milestones.GetByBoardAsync(board.Id, cancellationToken));
        // Общая веха приходит от каждого проекта, где она видна.
        return result.DistinctBy(m => m.Id).ToList();
    }

    public async Task<IReadOnlyList<FqlTeam>> TeamsAsync(CancellationToken cancellationToken)
    {
        var all = await groups.GetAllAsync(cancellationToken);
        var members = await groups.GetMemberIdsAsync(all.Select(g => g.Id).ToList(), cancellationToken);
        return all.Select(g => new FqlTeam(g.Id, g.Name, g.IsTeam, members.GetValueOrDefault(g.Id)?.Contains(actor.Id) == true)).ToList();
    }

    public Task<IReadOnlyList<Guid>> BlockedByAsync(Guid taskId, CancellationToken cancellationToken) =>
        links.GetBlockedTargetsAsync([taskId], cancellationToken);
}
