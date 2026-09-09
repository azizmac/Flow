namespace Flow.Domain.Entities;

/// <summary>
/// Задача на доске. Создаётся только через <see cref="Board.CreateTask"/>, никогда напрямую,
/// чтобы код задачи (<see cref="TaskCode"/>) и статус по умолчанию всегда были согласованы с доской.
/// </summary>
public sealed class TaskItem
{
    public Guid Id { get; private set; }

    public Guid BoardId { get; private set; }

    public TaskCode Code { get; private set; } = null!;

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid StatusId { get; private set; }

    /// <summary>Исполнитель (User.Id); null — не назначен. Активность пользователя проверяет вызывающая сторона, как и в ChangeStatus.</summary>
    public Guid? AssigneeId { get; private set; }

    /// <summary>Кто создал (User.Id). null — задачи, созданные до появления ролей. Нужен для «своей задачи» у Member.</summary>
    public Guid? CreatedById { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private TaskItem()
    {
        // EF Core
    }

    internal TaskItem(Guid boardId, TaskCode code, string title, string? description, Guid statusId, Guid? createdById)
    {
        Id = Guid.NewGuid();
        BoardId = boardId;
        Code = code;
        Title = ValidateTitle(title);
        Description = description;
        StatusId = statusId;
        CreatedById = createdById;
        CreatedAt = DateTime.UtcNow;
    }

    public void Rename(string title) => Title = ValidateTitle(title);

    public void UpdateDescription(string? description) => Description = description;

    /// <summary>
    /// Меняет статус задачи. Сама не проверяет, что <paramref name="statusId"/> принадлежит той же доске —
    /// эту проверку делает вызывающая сторона, у которой есть доступ к списку статусов доски
    /// (см. Board.Statuses / соответствующий эндпоинт).
    /// </summary>
    public void ChangeStatus(Guid statusId) => StatusId = statusId;

    /// <summary>
    /// Назначает исполнителя. Не проверяет, что пользователь существует и активен — у сущности нет доступа
    /// к пользователям; проверку делает Application (TaskAssignCommandHandler).
    /// </summary>
    public void Assign(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("Assignee id must not be empty.", nameof(userId));

        AssigneeId = userId;
    }

    public void Unassign() => AssigneeId = null;

    private static string ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Task title must not be empty.", nameof(title));

        return title.Trim();
    }
}
