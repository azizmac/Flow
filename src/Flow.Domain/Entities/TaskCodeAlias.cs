namespace Flow.Domain.Entities;

/// <summary>
/// Прежний код задачи, переехавшей в другой проект (docs/TZ_task_model.md §6). Код в ссылках, коммитах и чатах
/// должен продолжать вести на задачу: поиск по коду сначала смотрит живые коды, потом — алиасы.
/// Код хранится строкой (PK): у одного кода — одна задача.
/// </summary>
public sealed class TaskCodeAlias
{
    public string Code { get; private set; } = string.Empty;

    public Guid TaskId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private TaskCodeAlias()
    {
        // EF Core
    }

    public static TaskCodeAlias Create(TaskCode code, Guid taskId)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (taskId == Guid.Empty)
            throw new ArgumentException("Task id must not be empty.", nameof(taskId));

        return new TaskCodeAlias { Code = code.Value, TaskId = taskId, CreatedAt = DateTime.UtcNow };
    }

    /// <summary>Код уже вёл на другую задачу (проект с тем же ключом пересоздали) — теперь ведёт на эту.</summary>
    public void Repoint(Guid taskId)
    {
        if (taskId == Guid.Empty)
            throw new ArgumentException("Task id must not be empty.", nameof(taskId));

        TaskId = taskId;
    }
}
