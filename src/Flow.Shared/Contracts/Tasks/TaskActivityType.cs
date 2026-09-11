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
    AttachmentRemoved = 9
}
