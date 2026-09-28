namespace Flow.Domain.Entities;

/// <summary>
/// Тип задачи (эпик, история, ошибка…), настраиваемый на уровне проекта — как <see cref="Status"/>.
/// Создаётся только через <see cref="Board.AddTaskType"/>, чтобы инварианты «ровно один тип по умолчанию»
/// и «имя уникально в проекте» проверялись в одном месте. Удалять тип нельзя — только архивировать:
/// на него ссылаются задачи, а архивный тип просто не предлагается при создании.
/// </summary>
public sealed class TaskType
{
    public const int NameMaxLength = 50;

    public Guid Id { get; private set; }

    public Guid BoardId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public TaskTypeKind Kind { get; private set; }

    /// <summary>Порядок в выпадающих списках (0, 1, 2…); назначается при добавлении.</summary>
    public int SortOrder { get; private set; }

    public bool IsDefault { get; private set; }

    public bool IsArchived { get; private set; }

    /// <summary>
    /// Свой workflow типа (docs/TZ_workflow_config.md §2, этап 3E): режим, а переходы — StatusTransition с этим
    /// TaskTypeId. null — тип живёт по workflow проекта.
    /// </summary>
    public WorkflowMode? OwnWorkflowMode { get; private set; }

    public int Level => Kind.Level();

    private TaskType()
    {
        // EF Core
    }

    internal TaskType(Guid boardId, string name, TaskTypeKind kind, int sortOrder, bool isDefault)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentException($"Unknown task type kind {kind}.", nameof(kind));

        Id = Guid.NewGuid();
        BoardId = boardId;
        Name = ValidateName(name);
        Kind = kind;
        SortOrder = sortOrder;
        IsDefault = isDefault;
    }

    internal void Rename(string name) => Name = ValidateName(name);

    internal void SetDefault(bool isDefault) => IsDefault = isDefault;

    internal void SetArchived(bool isArchived) => IsArchived = isArchived;

    internal void SetOwnWorkflowMode(WorkflowMode? mode) => OwnWorkflowMode = mode;

    internal static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Task type name must not be empty.", nameof(name));

        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Task type name must be at most {NameMaxLength} characters.", nameof(name));

        return trimmed;
    }
}
