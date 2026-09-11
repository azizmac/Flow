namespace Flow.Shared.Contracts.Tasks;

/// <summary>
/// BoardId нужен странице /tasks/{id}: по нему клиент подгружает ключ и статусы проекта.
/// CreatedById — кто создал (null у задач до ролей); вместе с AssigneeId определяет «свою задачу» для Member.
/// DueDate — срок без времени, null — без срока. CommentCount — число комментариев (считается в списке задачи одним GROUP BY).
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
    int CommentCount);
