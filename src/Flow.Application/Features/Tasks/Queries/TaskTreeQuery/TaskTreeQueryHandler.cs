using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Application.Features.Tasks.Fql;
using Flow.Application.Security;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskTreeQuery;

internal sealed class TaskTreeQueryHandler(
    IBoardRepository boards,
    ITaskItemRepository tasks,
    TaskResponses responses,
    ActorResolver actors,
    IProjectAccess projectAccess,
    IUserRepository users,
    ITaskLinkRepository links)
    : IRequestHandler<TaskTreeQuery, IReadOnlyList<TaskTreeNode>?>
{
    public async Task<IReadOnlyList<TaskTreeNode>?> Handle(TaskTreeQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        if (!(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken)).CanView)
            return null;
        if (await boards.GetByIdAsync(request.BoardId, cancellationToken) is not { } board)
            return null;

        if (request.RootId is { } rootId && (await tasks.GetByIdAsync(rootId, cancellationToken))?.BoardId != request.BoardId)
            return null;

        FqlBound? fql = null;
        if (!string.IsNullOrWhiteSpace(request.Fql))
        {
            var lookup = new FqlLookup(actor, boards, users, tasks, links, projectAccess);
            fql = await FqlBinder.BindAsync(request.Fql, lookup, actor.Id, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
        }

        // Дерево целиком (поддерево RootId): фильтр и прогресс считаются по нему в памяти — связи «родитель → дети»
        // в SQL уже развёрнуты рекурсивным CTE, а поддерево проекта не бывает огромным настолько, чтобы это мешало.
        var entries = await tasks.GetTreeAsync(request.BoardId, request.RootId, cancellationToken);
        var byId = entries.ToDictionary(e => e.Task.Id, e => e.Task);
        var ids = byId.Keys.ToHashSet();
        var children = entries.Where(e => e.Task.ParentId is { } p && ids.Contains(p))
            .GroupBy(e => e.Task.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Task.Id).ToList());

        var filtered = request.AssigneeId is not null || request.Unassigned || !string.IsNullOrWhiteSpace(request.Query)
                       || request.TypeKind is not null || request.Priority is not null || request.StatusId is not null || fql is not null;
        var matched = filtered
            ? (await tasks.MatchingIdsAsync(new TaskListFilter(
                BoardId: request.BoardId,
                AssigneeId: request.AssigneeId,
                Unassigned: request.Unassigned,
                StatusId: request.StatusId,
                Query: request.Query,
                TypeKind: request.TypeKind?.ToDomainKind(),
                Priority: request.Priority?.ToDomainPriority(),
                Condition: fql?.Filter), cancellationToken)).ToHashSet()
            : ids;

        // Обход с конца — это «дети раньше родителей»: и видимость, и прогресс поддерева собираются за один проход.
        var finals = board.Statuses.Where(s => s.IsFinal).Select(s => s.Id).ToHashSet();
        var visible = new HashSet<Guid>();
        var progress = new Dictionary<Guid, TaskTreeProgress>();
        foreach (var entry in entries.Reverse())
        {
            var task = entry.Task;
            var own = children.GetValueOrDefault(task.Id) ?? [];
            if (matched.Contains(task.Id) || own.Any(visible.Contains))
                visible.Add(task.Id);

            var total = 0;
            var done = 0;
            var points = 0m;
            var donePoints = 0m;
            foreach (var child in own.Select(id => byId[id]))
            {
                var closed = finals.Contains(child.StatusId);
                var below = progress[child.Id];
                total += 1 + below.Total;
                done += (closed ? 1 : 0) + below.Done;
                points += (child.StoryPoints ?? 0) + below.Points;
                donePoints += (closed ? child.StoryPoints ?? 0 : 0) + below.DonePoints;
            }

            progress[task.Id] = new TaskTreeProgress(total, done, points, donePoints);
        }

        var shown = entries
            .Where(e => visible.Contains(e.Task.Id) && (request.MaxDepth is not { } max || e.Depth <= max))
            .ToList();

        // Счётчики карточек — одним GROUP BY на всё, что уходит клиенту, как у списка.
        var built = await responses.BuildAsync(shown.Select(e => e.Task).ToList(), cancellationToken);
        return shown.Select((e, i) => new TaskTreeNode(
                built[i],
                e.Depth,
                !matched.Contains(e.Task.Id),
                (children.GetValueOrDefault(e.Task.Id) ?? []).Count(visible.Contains),
                progress[e.Task.Id]))
            .ToList();
    }
}
