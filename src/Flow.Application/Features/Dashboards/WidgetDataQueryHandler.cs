using Flow.Application.Abstractions;
using Flow.Application.Features.Milestones;
using Flow.Application.Features.Sprints.Queries.SprintReportQuery;
using Flow.Application.Features.Tasks;
using Flow.Application.Features.Tasks.Fql;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Dashboards;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using DomainWidgetType = Flow.Domain.Entities.WidgetType;
using SharedWidgetType = Flow.Shared.Contracts.Dashboards.WidgetType;

namespace Flow.Application.Features.Dashboards;

/// <summary>
/// Данные виджета (docs/TZ_task_views.md §8) — правами смотрящего, а не автора: запрос тот же, видимость проектов своя.
/// Ошибка одного виджета (битый FQL, удалённый фильтр, нет активного спринта) возвращается в ответе, дашборд живёт.
/// Burndown и веха переиспользуют запросы отчёта спринта и прогресса вехи — со своими проверками видимости.
/// </summary>
internal sealed class WidgetDataQueryHandler(
    IDashboardRepository dashboards,
    ISavedFilterRepository filters,
    IBoardRepository boards,
    IUserRepository users,
    ITaskItemRepository tasks,
    ITaskLinkRepository links,
    ISprintRepository sprints,
    IMilestoneRepository milestones,
    TaskResponses responses,
    ActorResolver actors,
    IProjectAccess projectAccess,
    ISender sender) : IRequestHandler<WidgetDataQuery, WidgetDataResponse?>
{
    public async Task<WidgetDataResponse?> Handle(WidgetDataQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var dashboard = await dashboards.GetByIdAsync(request.DashboardId, cancellationToken);
        if (dashboard is null || !dashboard.IsVisibleTo(actor.Id) || dashboard.Widgets.FirstOrDefault(w => w.Id == request.WidgetId) is not { } widget)
            return null;

        var type = (SharedWidgetType)(int)widget.Type;
        var config = DashboardMapping.ConfigOf(widget);
        try
        {
            return widget.Type switch
            {
                DomainWidgetType.TaskList => await ListAsync(actor, config, type, cancellationToken),
                DomainWidgetType.Counter => await CounterAsync(actor, config, type, cancellationToken),
                DomainWidgetType.Breakdown => await BreakdownAsync(actor, config, type, cancellationToken),
                DomainWidgetType.CreatedVsClosed => await DailyAsync(actor, config, type, cancellationToken),
                DomainWidgetType.SprintBurndown => await BurndownAsync(actor, config, type, cancellationToken),
                DomainWidgetType.MilestoneProgress => await sender.Send(new MilestoneGetQuery(actor.Id, config.MilestoneId ?? Guid.Empty), cancellationToken) is { } m
                    ? new WidgetDataResponse(type, Milestone: m)
                    : new WidgetDataResponse(type, Error: "Веха не найдена или недоступна."),
                _ => new WidgetDataResponse(type, Text: config.Text ?? "")
            };
        }
        catch (FqlException ex)
        {
            return new WidgetDataResponse(type, Error: $"FQL: {ex.Message}");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new WidgetDataResponse(type, Error: ex.Message);
        }
    }

    /// <summary>Отбор задачного виджета: FQL или сохранённый фильтр (тоже видимый смотрящему), видимые ему проекты.</summary>
    private async Task<(TaskListFilter Filter, string? Fql)> FilterAsync(User actor, WidgetConfig config, int limit, CancellationToken ct)
    {
        var fql = config.Fql;
        if (config.FilterId is { } filterId)
        {
            var saved = await filters.GetByIdAsync(filterId, ct);
            if (saved is null || !saved.IsVisibleTo(actor.Id))
                throw new InvalidOperationException("Сохранённый фильтр удалён или больше не общий.");
            fql = saved.Query;
        }

        FqlBound? bound = null;
        if (!string.IsNullOrWhiteSpace(fql))
        {
            var lookup = new FqlLookup(actor, boards, users, tasks, links, projectAccess, sprints, milestones);
            bound = await FqlBinder.BindAsync(fql, lookup, actor.Id, DateOnly.FromDateTime(DateTime.UtcNow), ct);
        }

        var filter = new TaskListFilter(
            Limit: limit,
            Offset: 0,
            Sort: TaskSortField.Updated,
            VisibleBoardIds: await projectAccess.VisibleBoardIdsAsync(actor, ct),
            Condition: bound?.Filter,
            Orders: bound?.Orders is { Count: > 0 } orders ? orders : null);
        return (filter, fql);
    }

    private async Task<WidgetDataResponse> ListAsync(User actor, WidgetConfig config, SharedWidgetType type, CancellationToken ct)
    {
        var (filter, fql) = await FilterAsync(actor, config, config.Limit ?? 10, ct);
        var items = await tasks.SearchAsync(filter, ct);
        var count = (await tasks.CountAsync(filter, ct)).Matched;
        return new WidgetDataResponse(type, Tasks: await responses.BuildAsync(items, ct), Count: count, Fql: fql);
    }

    private async Task<WidgetDataResponse> CounterAsync(User actor, WidgetConfig config, SharedWidgetType type, CancellationToken ct)
    {
        var (filter, fql) = await FilterAsync(actor, config, 1, ct);
        return new WidgetDataResponse(type, Count: (await tasks.CountAsync(filter, ct)).Matched, Fql: fql);
    }

    /// <summary>Разбивка: один GROUP BY, подписи — из проектов, людей и вариантов; группы сверх семи — «Другие».</summary>
    private async Task<WidgetDataResponse> BreakdownAsync(User actor, WidgetConfig config, SharedWidgetType type, CancellationToken ct)
    {
        var (filter, fql) = await FilterAsync(actor, config, 1, ct);
        var visible = await new FqlLookup(actor, boards, users, tasks, links, projectAccess, sprints, milestones).VisibleBoardsAsync(ct);
        var groupBy = config.GroupBy ?? "status";

        TaskGroupField field;
        IReadOnlyList<Guid>? fieldIds = null;
        Func<string?, string> label;
        switch (groupBy)
        {
            case "status":
                field = TaskGroupField.Status;
                var statuses = visible.SelectMany(b => b.Statuses).ToDictionary(s => s.Id.ToString(), s => s.Name);
                label = key => key is not null && statuses.TryGetValue(key, out var n) ? n : "—";
                break;
            case "assignee":
                field = TaskGroupField.Assignee;
                label = _ => "";
                break;
            case "priority":
                field = TaskGroupField.Priority;
                label = key => key switch { "4" => "Критический", "3" => "Высокий", "2" => "Средний", "1" => "Низкий", _ => "Без приоритета" };
                break;
            case "type":
                field = TaskGroupField.Type;
                var types = visible.SelectMany(b => b.TaskTypes).ToDictionary(t => t.Id.ToString(), t => t.Name);
                label = key => key is not null && types.TryGetValue(key, out var n) ? n : "—";
                break;
            case "project":
                field = TaskGroupField.Board;
                var names = visible.ToDictionary(b => b.Id.ToString(), b => b.Name);
                label = key => key is not null && names.TryGetValue(key, out var n) ? n : "—";
                break;
            default:
                var key = groupBy["cf:".Length..];
                var fields = visible.SelectMany(b => b.CustomFields).Where(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)).ToList();
                if (fields.Count == 0)
                    throw new InvalidOperationException($"Поля «cf.{key}» нет ни в одном проекте.");
                field = TaskGroupField.CustomField;
                fieldIds = fields.Select(f => f.Id).ToList();
                var options = fields.SelectMany(f => f.Options).GroupBy(o => o.Id.ToString()).ToDictionary(g => g.Key, g => g.First().Label);
                label = value => value switch
                {
                    null => "Не заполнено",
                    _ when options.TryGetValue(value, out var n) => n,
                    "true" or "True" => "Да",
                    "false" or "False" => "Нет",
                    _ => value
                };
                break;
        }

        var groups = await tasks.GroupCountAsync(filter, field, fieldIds, ct);
        if (field == TaskGroupField.Assignee)
        {
            // Справочник людей невелик и общий — одним списком, как на клиенте.
            var people = (await users.ListAsync(includeInactive: true, ct))
                .ToDictionary(u => u.Id.ToString(), u => $"{u.FirstName} {u.LastName}".Trim());
            label = key => key is null ? "Не назначена" : people.TryGetValue(key, out var n) ? n : "—";
        }

        var ordered = groups.OrderByDescending(g => g.Count).ThenBy(g => label(g.Key), StringComparer.CurrentCulture).ToList();
        var result = ordered.Take(DashboardLimits.MaxGroups).Select(g => new WidgetGroup(g.Key, label(g.Key), g.Count)).ToList();
        if (ordered.Count > DashboardLimits.MaxGroups)
            result.Add(new WidgetGroup("other", "Другие", ordered.Skip(DashboardLimits.MaxGroups).Sum(g => g.Count)));
        return new WidgetDataResponse(type, Groups: result, Count: groups.Sum(g => g.Count), Fql: fql);
    }

    private async Task<WidgetDataResponse> DailyAsync(User actor, WidgetConfig config, SharedWidgetType type, CancellationToken ct)
    {
        var (filter, fql) = await FilterAsync(actor, config, 1, ct);
        var days = config.Days ?? 30;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-(days - 1));
        var counts = await tasks.DailyCountsAsync(filter, from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), ct);
        var points = Enumerable.Range(0, days)
            .Select(i => from.AddDays(i))
            .Select(d => new WidgetDayPoint(d, counts.Created.GetValueOrDefault(d), counts.Closed.GetValueOrDefault(d)))
            .ToList();
        return new WidgetDataResponse(type, Days: points, Fql: fql);
    }

    private async Task<WidgetDataResponse> BurndownAsync(User actor, WidgetConfig config, SharedWidgetType type, CancellationToken ct)
    {
        var sprintId = config.SprintId;
        if (sprintId is null && config.BoardId is { } boardId)
        {
            if (!(await projectAccess.GetAsync(actor, boardId, ct)).CanView)
                return new WidgetDataResponse(type, Error: "Проект не найден или недоступен.");
            sprintId = (await sprints.GetByBoardAsync(boardId, includeCompleted: false, ct)).FirstOrDefault(s => s.State == SprintState.Active)?.Id;
            if (sprintId is null)
                return new WidgetDataResponse(type, Error: "В проекте сейчас нет активного спринта.");
        }

        var report = await sender.Send(new SprintReportQuery(actor.Id, sprintId ?? Guid.Empty), ct);
        return report is null
            ? new WidgetDataResponse(type, Error: "Спринт не найден или недоступен.")
            : new WidgetDataResponse(type, Sprint: report);
    }
}
