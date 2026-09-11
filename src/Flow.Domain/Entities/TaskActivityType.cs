namespace Flow.Domain.Entities;

/// <summary>
/// Что именно изменилось в задаче (docs/TZ_task_activity_comments.md). Значения хранятся в БД как int —
/// порядок не менять, только дописывать в конец. AttachmentAdded/AttachmentRemoved зарезервированы под вложения.
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
    AttachmentRemoved = 9
}
