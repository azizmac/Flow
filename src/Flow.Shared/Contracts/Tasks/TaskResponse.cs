namespace Flow.Shared.Contracts.Tasks;

/// <summary>
/// BoardId нужен странице /tasks/{id}: по нему клиент подгружает ключ и статусы проекта.
/// CreatedById — кто создал (null у задач до ролей); вместе с AssigneeId определяет «свою задачу» для Member.
/// DueDate — срок без времени, null — без срока. CommentCount — число комментариев (считается в списке задачи одним GROUP BY).
/// TypeId — тип из BoardResponse.TaskTypes; StartDate ≤ DueDate; StoryPoints/EstimateMinutes — оценки, null — нет;
/// UpdatedAt — последнее изменение самой задачи (комментарии и вложения его не двигают).
/// ParentId — родитель в иерархии; ChildCount/ChildDoneCount — прямые подзадачи, всего и в финальном статусе
/// (в ответах команд не пересчитываются и равны 0, как CommentCount). BlockedByCount — сколько незакрытых задач
/// блокирует эту (тоже 0 в ответах команд); ChecklistDone/ChecklistTotal — прогресс чек-листа, точные везде.
/// </summary>
public sealed record TaskResponse(
    Guid Id,
    Guid BoardId,
    string Code,
    string Title,
    string? Description,
    Guid StatusId,
    Guid? AssigneeId,
    DateTime CreatedAt,
    Guid? CreatedById,
    DateOnly? DueDate,
    int CommentCount,
    Guid TypeId,
    TaskPriority Priority,
    DateOnly? StartDate,
    decimal? StoryPoints,
    int? EstimateMinutes,
    DateTime UpdatedAt,
    Guid? ParentId = null,
    int ChildCount = 0,
    int ChildDoneCount = 0,
    int BlockedByCount = 0,
    int ChecklistDone = 0,
    int ChecklistTotal = 0,
    Guid? SprintId = null);
