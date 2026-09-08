using Flow.Shared.Ids;

namespace Flow.Domain.Entities;

/// <summary>
/// Задача на доске. Создаётся только через <see cref="Board.CreateTask"/>, никогда напрямую,
/// чтобы код задачи (<see cref="TaskCode"/>) и статус по умолчанию всегда были согласованы с доской.
/// </summary>
public sealed class TaskItem
{
    public TaskId Id { get; private set; } = null!;

    public BoardId BoardId { get; private set; } = null!;

    public TaskCode Code { get; private set; } = null!;

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public StatusId StatusId { get; private set; } = null!;

    public DateTime CreatedAt { get; private set; }

    private TaskItem()
    {
        // EF Core
    }

    internal TaskItem(BoardId boardId, TaskCode code, string title, string? description, StatusId statusId)
    {
        Id = TaskId.New();
        BoardId = boardId;
        Code = code;
        Title = ValidateTitle(title);
        Description = description;
        StatusId = statusId;
        CreatedAt = DateTime.UtcNow;
    }

    public void Rename(string title) => Title = ValidateTitle(title);

    public void UpdateDescription(string? description) => Description = description;

    /// <summary>
    /// Меняет статус задачи. Сама не проверяет, что <paramref name="statusId"/> принадлежит той же доске —
    /// эту проверку делает вызывающая сторона, у которой есть доступ к списку статусов доски
    /// (см. Board.Statuses / соответствующий эндпоинт).
    /// </summary>
    public void ChangeStatus(StatusId statusId) => StatusId = statusId;

    private static string ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Task title must not be empty.", nameof(title));

        return title.Trim();
    }
}
