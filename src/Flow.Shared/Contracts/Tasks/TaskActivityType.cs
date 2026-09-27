namespace Flow.Shared.Contracts.Tasks;

/// <summary>Зеркало Flow.Domain.Entities.TaskActivityType (Shared не ссылается на Domain). Значения совпадают.</summary>
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
    MilestoneChanged = 22
}
