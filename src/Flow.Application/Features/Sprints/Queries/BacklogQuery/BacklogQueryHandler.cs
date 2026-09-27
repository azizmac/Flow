using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Application.Features.Tasks;
using Flow.Application.Features.Tasks.Fql;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Sprints;
using MediatR;

namespace Flow.Application.Features.Sprints.Queries.BacklogQuery;

internal sealed class BacklogQueryHandler(
    IBoardRepository boards,
    ITaskItemRepository tasks,
    ISprintRepository sprints,
    IMilestoneRepository milestones,
    TaskResponses responses,
    ActorResolver actors,
    IProjectAccess projectAccess,
    IUserRepository users,
    ITaskLinkRepository links)
    : IRequestHandler<BacklogQuery, BacklogResponse?>
{
    public async Task<BacklogResponse?> Handle(BacklogQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        if (!(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken)).CanView)
            return null;
        if (await boards.GetByIdAsync(request.BoardId, cancellationToken) is not { } board)
            return null;

        // Все задачи проекта одним обходом дерева: он же даёт родителей для колонки «Эпик».
        var entries = await tasks.GetTreeAsync(board.Id, null, cancellationToken);
        var byId = entries.ToDictionary(e => e.Task.Id, e => e.Task);
        var epicType = board.TaskTypes.Where(t => t.Kind == TaskTypeKind.Epic).Select(t => t.Id).ToHashSet();

        Guid? EpicOf(TaskItem task)
        {
            for (var current = task; current is not null; current = current.ParentId is { } p ? byId.GetValueOrDefault(p) : null)
            {
                if (epicType.Contains(current.TypeId) && current.Id != task.Id)
                    return current.Id;
            }

            return null;
        }

        var shown = entries.Select(e => e.Task).Where(t => !epicType.Contains(t.TypeId)).ToList();
        if (await MatchedAsync(request, actor, cancellationToken) is { } matched)
            shown = shown.Where(t => matched.Contains(t.Id)).ToList();

        // Панель эпиков считает задачи до фильтра по эпику — иначе выбор одного обнулил бы счётчики остальных.
        var epicCounts = shown.GroupBy(EpicOf).Where(g => g.Key is not null).ToDictionary(g => g.Key!.Value, g => g.Count());
        if (request.EpicId is { } epicId)
            shown = shown.Where(t => EpicOf(t) == epicId).ToList();

        var finals = board.Statuses.Where(s => s.IsFinal).Select(s => s.Id).ToHashSet();
        var open = await sprints.GetByBoardAsync(board.Id, includeCompleted: false, cancellationToken);
        var groups = open.Select(s => (Sprint: (Sprint?)s, Tasks: shown.Where(t => t.SprintId == s.Id).ToList()))
            .Append((Sprint: null, Tasks: shown.Where(t => t.SprintId is null && !finals.Contains(t.StatusId)).ToList()))
            .Select(g => (g.Sprint, Tasks: g.Tasks.OrderBy(t => t.Rank, StringComparer.Ordinal).ToList()))
            .ToList();

        // Счётчики карточек — одним набором GROUP BY на всё, что уходит клиенту.
        var built = (await responses.BuildAsync(groups.SelectMany(g => g.Tasks).ToList(), cancellationToken)).ToDictionary(t => t.Id);

        var sections = groups.Select(g => new BacklogSection(
                g.Sprint?.ToResponse(),
                g.Tasks.Select(t => new BacklogItem(built[t.Id], EpicOf(t))).ToList(),
                g.Tasks.Sum(t => t.StoryPoints ?? 0),
                g.Tasks.Sum(t => t.EstimateMinutes ?? 0),
                g.Tasks.GroupBy(t => t.AssigneeId)
                    .Select(a => new BacklogAssigneeLoad(a.Key, a.Count(), a.Sum(t => t.StoryPoints ?? 0)))
                    .OrderByDescending(a => a.Points).ThenByDescending(a => a.Count)
                    .ToList()))
            .ToList();

        var epics = entries.Select(e => e.Task)
            .Where(t => epicType.Contains(t.TypeId) && !finals.Contains(t.StatusId))
            .Select(t => new BacklogEpic(t.Id, t.Code.Value, t.Title, epicCounts.GetValueOrDefault(t.Id)))
            .ToList();

        return new BacklogResponse(board.Id, sections, epics);
    }

    /// <summary>Подходящие Id тем же SQL-фильтром, что у списка; null — фильтров нет.</summary>
    private async Task<HashSet<Guid>?> MatchedAsync(BacklogQuery request, User actor, CancellationToken cancellationToken)
    {
        FqlBound? fql = null;
        if (!string.IsNullOrWhiteSpace(request.Fql))
        {
            var lookup = new FqlLookup(actor, boards, users, tasks, links, projectAccess, sprints, milestones);
            fql = await FqlBinder.BindAsync(request.Fql, lookup, actor.Id, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
        }

        if (request.AssigneeId is null && !request.Unassigned && string.IsNullOrWhiteSpace(request.Query)
            && request.TypeKind is null && request.Priority is null && fql is null)
            return null;

        return (await tasks.MatchingIdsAsync(new TaskListFilter(
            BoardId: request.BoardId,
            AssigneeId: request.AssigneeId,
            Unassigned: request.Unassigned,
            Query: request.Query,
            TypeKind: request.TypeKind?.ToDomainKind(),
            Priority: request.Priority?.ToDomainPriority(),
            Condition: fql?.Filter), cancellationToken)).ToHashSet();
    }
}
