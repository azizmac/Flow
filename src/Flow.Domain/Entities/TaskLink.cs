namespace Flow.Domain.Entities;

/// <summary>
/// Связь между двумя задачами (docs/TZ_task_model.md §5). Отдельная сущность, а не коллекция задачи: задачи могут
/// быть в разных проектах, и связь принадлежит обеим сразу. Удаляется физически, след — в журнале обеих задач.
/// Права и проверка «вторая задача видна» — в Application.
/// </summary>
public sealed class TaskLink
{
    public Guid Id { get; private set; }

    public Guid SourceTaskId { get; private set; }

    public Guid TargetTaskId { get; private set; }

    public TaskLinkType Type { get; private set; }

    public Guid CreatedById { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private TaskLink()
    {
        // EF Core
    }

    private TaskLink(Guid sourceTaskId, Guid targetTaskId, TaskLinkType type, Guid createdById)
    {
        Id = Guid.NewGuid();
        SourceTaskId = sourceTaskId;
        TargetTaskId = targetTaskId;
        Type = type;
        CreatedById = createdById;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Единственная точка создания. Связь на саму себя — ошибка. <see cref="TaskLinkType.RelatesTo"/> симметрична:
    /// концы упорядочиваются (меньший Id — источник), иначе A→B и B→A были бы двумя связями, а unique
    /// (SourceTaskId, TargetTaskId, Type) их не поймал бы.
    /// </summary>
    public static TaskLink Create(Guid sourceTaskId, Guid targetTaskId, TaskLinkType type, Guid createdById)
    {
        if (sourceTaskId == Guid.Empty || targetTaskId == Guid.Empty)
            throw new ArgumentException("Task ids must not be empty.", nameof(sourceTaskId));
        if (sourceTaskId == targetTaskId)
            throw new InvalidOperationException("A task cannot be linked to itself.");
        if (!Enum.IsDefined(type))
            throw new ArgumentException($"Unknown link type {type}.", nameof(type));
        if (createdById == Guid.Empty)
            throw new ArgumentException("Creator id must not be empty.", nameof(createdById));

        if (type == TaskLinkType.RelatesTo && sourceTaskId.CompareTo(targetTaskId) > 0)
            (sourceTaskId, targetTaskId) = (targetTaskId, sourceTaskId);

        return new TaskLink(sourceTaskId, targetTaskId, type, createdById);
    }

    /// <summary>Второй конец связи для задачи <paramref name="taskId"/>.</summary>
    public Guid OtherTaskId(Guid taskId) => taskId == SourceTaskId ? TargetTaskId : SourceTaskId;

    /// <summary>Связь смотрит «наружу» от этой задачи (она источник): «блокирует», а не «заблокирована».</summary>
    public bool IsOutwardFor(Guid taskId) => taskId == SourceTaskId;
}
