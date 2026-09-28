namespace Flow.Shared.Contracts.Milestones;

/// <summary>Зеркало Domain.MilestoneState.</summary>
public enum MilestoneState
{
    Open = 0,
    Closed = 1
}

/// <summary>
/// Прогресс вехи (docs/TZ_task_views.md §6): задачи всего / закрыто / в работе (вид статуса «в работе» или «на
/// проверке»), то же в story points, просроченные незакрытые. Forecast — ожидаемая дата закрытия по средней скорости
/// за последние 14 дней (ClosedRecently задач); null — всё закрыто или скорости нет.
/// </summary>
public sealed record MilestoneProgress(
    int Total,
    int Done,
    int InProgress,
    decimal Points,
    decimal DonePoints,
    int Overdue,
    int ClosedRecently,
    DateOnly? Forecast);

/// <summary>
/// Веха проекта с прогрессом. BoardId — проект-владелец; SharedBoardIds — другие проекты, где веха тоже доступна
/// (общая веха, этап 2H); прогресс считается по задачам всех этих проектов.
/// </summary>
public sealed record MilestoneResponse(
    Guid Id,
    Guid BoardId,
    string Name,
    string? Description,
    DateOnly? TargetDate,
    MilestoneState State,
    DateTime? ClosedAt,
    int SortOrder,
    MilestoneProgress Progress,
    IReadOnlyList<Guid>? SharedBoardIds = null);

/// <summary>PUT /milestones/{id}/boards: проекты, с которыми веха общая (весь список; владелец не указывается).</summary>
public sealed record ShareMilestoneRequest(IReadOnlyList<Guid> BoardIds);

/// <summary>POST /boards/{id}/milestones.</summary>
public sealed record CreateMilestoneRequest(string Name, string? Description = null, DateOnly? TargetDate = null);

/// <summary>
/// PATCH /milestones/{id}: null — не трогать; ClearDescription/ClearTargetDate снимают; Closed = true закрывает,
/// false — открывает снова.
/// </summary>
public sealed record UpdateMilestoneRequest(
    string? Name = null,
    string? Description = null,
    bool ClearDescription = false,
    DateOnly? TargetDate = null,
    bool ClearTargetDate = false,
    bool? Closed = null);

/// <summary>PATCH /tasks/{id}/milestone: null — снять веху.</summary>
public sealed record SetTaskMilestoneRequest(Guid? MilestoneId);
