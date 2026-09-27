namespace Flow.Domain.Entities;

/// <summary>
/// Что именно изменилось в задаче (docs/TZ_task_activity_comments.md). Значения хранятся в БД как int —
/// порядок не менять, только дописывать в конец.
/// </summary>
public enum TaskActivityType
{
    Created = 0,
    TitleChanged = 1,
    DescriptionChanged = 2,
    StatusChanged = 3,
    AssigneeChanged = 4,
    DueDateChanged = 5,
    CommentAdded = 6,
    CommentDeleted = 7,
    AttachmentAdded = 8,
    AttachmentRemoved = 9,
    PriorityChanged = 10,
    StartDateChanged = 11,
    StoryPointsChanged = 12,
    EstimateChanged = 13,
    TypeChanged = 14,
    ParentChanged = 15,
    ChildAdded = 16,
    ChildRemoved = 17,
    LinkAdded = 18,
    LinkRemoved = 19,
    ChecklistChanged = 20,

    /// <summary>Спринт задачи: OldValue/NewValue — Guid спринта, null — бэклог.</summary>
    SprintChanged = 21,

    /// <summary>Веха задачи: OldValue/NewValue — Guid вехи, null — без вехи.</summary>
    MilestoneChanged = 22,

    /// <summary>Пользовательское поле: OldValue — прежнее значение JSON, NewValue — {"field": Id поля, "value": новое}.</summary>
    CustomFieldChanged = 23,

    /// <summary>Слияние (docs/TZ_task_model.md §6) — у обеих задач: OldValue — Guid влитой (source), NewValue — Guid основной (target).</summary>
    Merged = 24,

    /// <summary>Разделение — у исходной задачи: NewValue — коды новых задач через запятую.</summary>
    Split = 25,

    /// <summary>Перенос в другой проект: OldValue — прежний код, NewValue — новый.</summary>
    Moved = 26
}
