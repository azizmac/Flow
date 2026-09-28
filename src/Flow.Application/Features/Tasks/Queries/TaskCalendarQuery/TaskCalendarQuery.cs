using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Application.Features.Tasks.Fql;
using Flow.Application.Security;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskCalendarQuery;

/// <summary>
/// Календарь (docs/TZ_task_views.md §5): задачи, чей отрезок [StartDate ?? DueDate, DueDate ?? StartDate] пересекает
/// окно [From, To], без пагинации — с жёстким лимитом <see cref="TaskCalendarQueryHandler.MaxItems"/> и флагом
/// Truncated. Задачи без дат в календарь не попадают. Фильтры — как у списка (панель и FQL; ORDER BY FQL не действует).
/// BoardId = null — все видимые проекты; невидимый проект — null (404).
/// </summary>
public sealed record TaskCalendarQuery(
    Guid ActorId,
    DateOnly From,
    DateOnly To,
    Guid? BoardId = null,
    Guid? AssigneeId = null,
    bool Unassigned = false,
    string? Query = null,
    TaskTypeKind? TypeKind = null,
    TaskPriority? Priority = null,
    string? Fql = null) : IRequest<TaskCalendarResponse?>;

internal sealed class TaskCalendarQueryHandler(
    ITaskItemRepository tasks,
    TaskResponses responses,
    ActorResolver actors,
    IProjectAccess projectAccess,
    IBoardRepository boards,
    IUserRepository users,
    ITaskLinkRepository links,
    ISprintRepository sprints,
    IMilestoneRepository milestones, IGroupRepository groupDirectory)
    : IRequestHandler<TaskCalendarQuery, TaskCalendarResponse?>
{
    public const int MaxItems = 1000;

    /// <summary>Окно шире квартала календарю не нужно (месяц с хвостами соседних — 6 недель).</summary>
    private const int MaxDays = 100;

    public async Task<TaskCalendarResponse?> Handle(TaskCalendarQuery request, CancellationToken cancellationToken)
    {
        if (request.To < request.From || request.To.DayNumber - request.From.DayNumber > MaxDays)
            throw new ArgumentException($"Окно календаря — от 1 до {MaxDays} дней, конец не раньше начала.", nameof(request.To));

        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        if (request.BoardId is { } boardId
            && (!(await projectAccess.GetAsync(actor, boardId, cancellationToken)).CanView || await boards.GetByIdAsync(boardId, cancellationToken) is null))
            return null;

        TaskFilterNode window = Window(request.From, request.To);
        if (!string.IsNullOrWhiteSpace(request.Fql))
        {
            var lookup = new FqlLookup(actor, boards, users, tasks, links, projectAccess, sprints, milestones, groupDirectory);
            var fql = await FqlBinder.BindAsync(request.Fql, lookup, actor.Id, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
            if (fql.Filter is { } condition)
                window = new TaskFilterAnd([window, condition]);
        }

        var filter = new TaskListFilter(
            BoardId: request.BoardId,
            AssigneeId: request.AssigneeId,
            Unassigned: request.Unassigned,
            Query: request.Query,
            Limit: MaxItems + 1,
            Offset: 0,
            Sort: TaskSortField.Due,
            TypeKind: request.TypeKind?.ToDomainKind(),
            Priority: request.Priority?.ToDomainPriority(),
            VisibleBoardIds: await projectAccess.VisibleBoardIdsAsync(actor, cancellationToken),
            Condition: window);

        var found = await tasks.SearchAsync(filter, cancellationToken);
        var truncated = found.Count > MaxItems;
        var items = await responses.BuildAsync(found.Take(MaxItems).ToList(), cancellationToken);
        return new TaskCalendarResponse(request.From, request.To, items, truncated);
    }

    /// <summary>
    /// Отрезок задачи пересекает окно: со сроком — срок не раньше начала окна и (начало ?? срок) не позже конца;
    /// без срока — дата начала внутри окна (одна дата — точка).
    /// </summary>
    internal static TaskFilterNode Window(DateOnly from, DateOnly to) => new TaskFilterOr([
        new TaskFilterAnd([
            new TaskFilterNot(new TaskFilterIsEmpty(TaskFilterNullable.DueDate)),
            new TaskFilterCompare(TaskFilterScalar.DueDate, TaskFilterOp.Gte, from),
            new TaskFilterOr([
                new TaskFilterAnd([new TaskFilterIsEmpty(TaskFilterNullable.StartDate), new TaskFilterCompare(TaskFilterScalar.DueDate, TaskFilterOp.Lte, to)]),
                new TaskFilterCompare(TaskFilterScalar.StartDate, TaskFilterOp.Lte, to)
            ])
        ]),
        new TaskFilterAnd([
            new TaskFilterIsEmpty(TaskFilterNullable.DueDate),
            new TaskFilterCompare(TaskFilterScalar.StartDate, TaskFilterOp.Gte, from),
            new TaskFilterCompare(TaskFilterScalar.StartDate, TaskFilterOp.Lte, to)
        ])
    ]);
}
