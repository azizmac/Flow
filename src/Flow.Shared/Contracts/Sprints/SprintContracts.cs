using Flow.Shared.Contracts.Tasks;

namespace Flow.Shared.Contracts.Sprints;

/// <summary>Зеркало Domain.SprintState.</summary>
public enum SprintState
{
    Planned = 0,
    Active = 1,
    Completed = 2
}

/// <summary>Спринт проекта (docs/TZ_task_views.md §2).</summary>
public sealed record SprintResponse(
    Guid Id,
    Guid BoardId,
    string Name,
    string? Goal,
    DateOnly? StartDate,
    DateOnly? EndDate,
    SprintState State,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int SortOrder);

/// <summary>POST /boards/{id}/sprints: без имени — «Спринт N» по счёту в проекте.</summary>
public sealed record CreateSprintRequest(string? Name = null, string? Goal = null, DateOnly? StartDate = null, DateOnly? EndDate = null);

/// <summary>PATCH /sprints/{id}: null — не трогать; ClearGoal/ClearDates снимают. У завершённого — только имя.</summary>
public sealed record UpdateSprintRequest(
    string? Name = null,
    string? Goal = null,
    bool ClearGoal = false,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    bool ClearDates = false);

/// <summary>POST /sprints/{id}/start.</summary>
public sealed record StartSprintRequest(DateOnly StartDate, DateOnly EndDate);

/// <summary>POST /sprints/{id}/complete: незакрытые задачи — в спринт MoveOpenTo, null — в бэклог.</summary>
public sealed record CompleteSprintRequest(Guid? MoveOpenTo = null);

/// <summary>PATCH /tasks/{id}/sprint: null — в бэклог.</summary>
public sealed record SetTaskSprintRequest(Guid? SprintId);

/// <summary>Нагрузка исполнителя в секции: задач и story points; AssigneeId = null — без исполнителя.</summary>
public sealed record BacklogAssigneeLoad(Guid? AssigneeId, int Count, decimal Points);

/// <summary>Строка бэклога: задача и её эпик (предок уровня 1), если есть.</summary>
public sealed record BacklogItem(TaskResponse Task, Guid? EpicId);

/// <summary>
/// Секция бэклога: спринт (активный, потом запланированные) или сам бэклог (Sprint = null — задачи без спринта,
/// не закрытые). Points и EstimateMinutes — суммы по секции, ByAssignee — распределение.
/// </summary>
public sealed record BacklogSection(
    SprintResponse? Sprint,
    IReadOnlyList<BacklogItem> Items,
    decimal Points,
    int EstimateMinutes,
    IReadOnlyList<BacklogAssigneeLoad> ByAssignee);

/// <summary>Эпик проекта для панели фильтра: сколько задач под ним видно в бэклоге.</summary>
public sealed record BacklogEpic(Guid Id, string Code, string Title, int Count);

public sealed record BacklogResponse(Guid BoardId, IReadOnlyList<BacklogSection> Sections, IReadOnlyList<BacklogEpic> Epics);

/// <summary>Строка отчёта спринта. Code/Title = null — задача удалена после снимка.</summary>
public sealed record SprintReportTask(Guid TaskId, string? Code, string? Title, decimal? Points, bool Committed, bool Done);

/// <summary>Группа отчёта: задач и story points.</summary>
public sealed record SprintReportTotals(int Count, decimal Points);

/// <summary>
/// Точка burndown на конец дня: осталось story points (незакрытые задачи в спринте) и идеальная линия.
/// Remaining = null — день ещё не наступил (или спринт уже завершён): идеальная линия видна на весь спринт сразу.
/// </summary>
public sealed record BurndownPoint(DateOnly Date, decimal? Remaining, decimal Ideal);

/// <summary>
/// Отчёт спринта (docs/TZ_task_views.md §2): взято на старте (снимок), добавлено по ходу, сделано, не сделано;
/// Tasks — все задачи отчёта; Burndown — каждый день спринта от начала до конца.
/// </summary>
public sealed record SprintReportResponse(
    SprintResponse Sprint,
    SprintReportTotals Committed,
    SprintReportTotals Added,
    SprintReportTotals Done,
    SprintReportTotals NotDone,
    IReadOnlyList<SprintReportTask> Tasks,
    IReadOnlyList<BurndownPoint> Burndown);
