using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Application.Features.Tasks.Fql;
using Flow.Application.Security;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using DomainStatusType = Flow.Domain.Entities.StatusType;
using SharedStatusType = Flow.Shared.Contracts.Boards.StatusType;

namespace Flow.Application.Features.Tasks.Queries.TaskBoardQuery;

internal sealed class TaskBoardQueryHandler(
    ITaskItemRepository tasks,
    TaskResponses responses,
    ActorResolver actors,
    IProjectAccess projectAccess,
    IBoardRepository boards,
    IUserRepository users,
    ITaskLinkRepository links,
    ISprintRepository sprints,
    IMilestoneRepository milestones)
    : IRequestHandler<TaskBoardQuery, TaskBoardResponse?>
{
    public const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    private static readonly DomainStatusType[] Types = Enum.GetValues<DomainStatusType>();

    public async Task<TaskBoardResponse?> Handle(TaskBoardQuery request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit ?? DefaultLimit, 1, MaxLimit);
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        Domain.Entities.Board? board = null;
        if (request.BoardId is { } boardId)
        {
            if (!(await projectAccess.GetAsync(actor, boardId, cancellationToken)).CanView)
                return null;

            board = await boards.GetByIdAsync(boardId, cancellationToken);
            if (board is null)
                return null;
        }

        FqlBound? fql = null;
        if (!string.IsNullOrWhiteSpace(request.Fql))
        {
            var lookup = new FqlLookup(actor, boards, users, tasks, links, projectAccess, sprints, milestones);
            fql = await FqlBinder.BindAsync(request.Fql, lookup, actor.Id, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
        }

        var baseFilter = new TaskListFilter(
            BoardId: request.BoardId,
            AssigneeId: request.AssigneeId,
            Unassigned: request.Unassigned,
            Query: request.Query,
            Limit: limit,
            Offset: Math.Max(0, request.Offset),
            // В проекте порядок колонки — ручной (ранг), между проектами ранги несравнимы — там по изменению.
            Sort: board is null ? TaskSortField.Updated : TaskSortField.Rank,
            Descending: board is null,
            TypeKind: request.TypeKind?.ToDomainKind(),
            Priority: request.Priority?.ToDomainPriority(),
            VisibleBoardIds: await projectAccess.VisibleBoardIdsAsync(actor, cancellationToken),
            Condition: fql?.Filter,
            DoneWindowAt: DateTime.UtcNow);

        // Колонки и фильтр каждой; счётчики — одним проходом по всей доске (он же знает окно финальных).
        var columns = board is not null
            ? board.Statuses.OrderBy(s => s.SortOrder)
                .Select(s => new Column(s.Id, null, false, baseFilter with { StatusId = s.Id }))
                .ToList()
            : Types.Select(t => new Column(null, t, false, baseFilter with { StatusType = t }))
                .Append(new Column(null, null, true, baseFilter with { UntypedStatus = true }))
                .ToList();

        if (request.StatusId is not null || request.StatusType is not null || request.Other)
        {
            columns = columns
                .Where(c => request.Other ? c.Other : c.StatusId == request.StatusId && c.StatusType == (DomainStatusType?)request.StatusType && !c.Other)
                .ToList();
            if (columns.Count == 0)
                throw new ArgumentException("The requested column does not exist.", nameof(request.StatusId));
        }

        var counts = await tasks.CountAsync(baseFilter, cancellationToken);
        var byStatus = counts.ByStatus.ToDictionary(x => x.StatusId, x => x.Count);

        var pages = new List<(Column Column, IReadOnlyList<Domain.Entities.TaskItem> Items)>();
        foreach (var column in columns)
            pages.Add((column, await tasks.SearchAsync(column.Filter, cancellationToken)));

        // Счётчики карточек (комментарии, подзадачи, чек-лист) — одним набором GROUP BY на все колонки сразу.
        var built = (await responses.BuildAsync(pages.SelectMany(p => p.Items).ToList(), cancellationToken)).ToDictionary(t => t.Id);

        var result = pages.Select(p => new TaskBoardColumn(
                p.Column.StatusId,
                (SharedStatusType?)p.Column.StatusType,
                p.Column.Other,
                p.Column.StatusId is { } id
                    ? byStatus.GetValueOrDefault(id)
                    : counts.ByType.Where(x => x.Type == (p.Column.Other ? null : p.Column.StatusType)).Sum(x => x.Count),
                p.Items.Select(t => built[t.Id]).ToList()))
            .ToList();

        return new TaskBoardResponse(board?.Id, board?.DoneColumnDays, result);
    }

    private sealed record Column(Guid? StatusId, DomainStatusType? StatusType, bool Other, TaskListFilter Filter);
}
