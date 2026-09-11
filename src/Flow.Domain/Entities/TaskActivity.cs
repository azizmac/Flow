using System.Globalization;

namespace Flow.Domain.Entities;

/// <summary>
/// Запись журнала изменений задачи: кто, когда и что поменял. Append-only — методов мутации нет.
/// Создаётся только фабриками по типу, чтобы <see cref="Type"/> и <see cref="OldValue"/>/<see cref="NewValue"/>
/// всегда были согласованы: для статуса и исполнителя — Guid строкой, для срока — ISO-дата, для названия — текст,
/// для описания — ничего (только факт). Пишет Application в той же транзакции, что и само изменение.
/// </summary>
public sealed class TaskActivity
{
    public const string DateFormat = "yyyy-MM-dd";

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    /// <summary>Кто сделал изменение (User.Id).</summary>
    public Guid ActorId { get; private set; }

    public TaskActivityType Type { get; private set; }

    public string? OldValue { get; private set; }

    public string? NewValue { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private TaskActivity()
    {
        // EF Core
    }

    private TaskActivity(Guid taskId, Guid actorId, TaskActivityType type, string? oldValue, string? newValue)
    {
        if (taskId == Guid.Empty)
            throw new ArgumentException("Task id must not be empty.", nameof(taskId));
        if (actorId == Guid.Empty)
            throw new ArgumentException("Actor id must not be empty.", nameof(actorId));

        Id = Guid.NewGuid();
        TaskId = taskId;
        ActorId = actorId;
        Type = type;
        OldValue = oldValue;
        NewValue = newValue;
        CreatedAt = DateTime.UtcNow;
    }

    public static TaskActivity Created(Guid taskId, Guid actorId) =>
        new(taskId, actorId, TaskActivityType.Created, null, null);

    public static TaskActivity TitleChanged(Guid taskId, Guid actorId, string oldTitle, string newTitle) =>
        new(taskId, actorId, TaskActivityType.TitleChanged, oldTitle, newTitle);

    public static TaskActivity DescriptionChanged(Guid taskId, Guid actorId) =>
        new(taskId, actorId, TaskActivityType.DescriptionChanged, null, null);

    public static TaskActivity StatusChanged(Guid taskId, Guid actorId, Guid oldStatusId, Guid newStatusId) =>
        new(taskId, actorId, TaskActivityType.StatusChanged, oldStatusId.ToString(), newStatusId.ToString());

    /// <summary>null с любой стороны — «не назначен».</summary>
    public static TaskActivity AssigneeChanged(Guid taskId, Guid actorId, Guid? oldAssigneeId, Guid? newAssigneeId) =>
        new(taskId, actorId, TaskActivityType.AssigneeChanged, oldAssigneeId?.ToString(), newAssigneeId?.ToString());

    /// <summary>null с любой стороны — «без срока».</summary>
    public static TaskActivity DueDateChanged(Guid taskId, Guid actorId, DateOnly? oldDueDate, DateOnly? newDueDate) =>
        new(taskId, actorId, TaskActivityType.DueDateChanged, FormatDate(oldDueDate), FormatDate(newDueDate));

    public static TaskActivity CommentAdded(Guid taskId, Guid actorId, Guid commentId) =>
        new(taskId, actorId, TaskActivityType.CommentAdded, null, commentId.ToString());

    public static TaskActivity CommentDeleted(Guid taskId, Guid actorId, Guid commentId) =>
        new(taskId, actorId, TaskActivityType.CommentDeleted, commentId.ToString(), null);

    private static string? FormatDate(DateOnly? date) =>
        date?.ToString(DateFormat, CultureInfo.InvariantCulture);
}
