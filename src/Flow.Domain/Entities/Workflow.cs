namespace Flow.Domain.Entities;

/// <summary>
/// Режим workflow проекта (docs/TZ_workflow_config.md §2). Free — статус меняется на любой, как было всегда;
/// Restricted — только по переходам графа. Хранится как int — только дописывать.
/// </summary>
public enum WorkflowMode
{
    Free = 0,
    Restricted = 1
}

/// <summary>
/// Условия перехода — все должны выполняться. MinRole — кто может выполнить переход (Admin+ workflow не обходит:
/// обход — это переход с MinRole = Admin в самом графе). RequireFields — пользовательские поля проекта, которые
/// должны быть заполнены (этап 3C): «в “На проверке” — только с заполненными “Шагами воспроизведения”».
/// </summary>
public sealed record TransitionConditions(
    ProjectRole? MinRole = null,
    bool RequireAssignee = false,
    bool RequireChildrenDone = false,
    bool RequireChecklistDone = false,
    IReadOnlyList<Guid>? RequireFields = null)
{
    public static readonly TransitionConditions None = new();
}

/// <summary>
/// Разрешённый переход между статусами проекта (часть агрегата Board). FromStatusId = null — «из любого статуса»
/// (типичный случай — «в Отменена откуда угодно»). Создаётся и заменяется только через <see cref="Board.SetWorkflow"/>.
/// </summary>
public sealed class StatusTransition
{
    public const int NameMaxLength = 60;

    public Guid Id { get; private set; }

    public Guid BoardId { get; private set; }

    /// <summary>Чей это переход: null — workflow проекта, иначе — свой workflow этого типа задачи (этап 3E).</summary>
    public Guid? TaskTypeId { get; private set; }

    public Guid? FromStatusId { get; private set; }

    public Guid ToStatusId { get; private set; }

    /// <summary>Подпись кнопки перехода («Взять в работу»); null — подписью служит имя статуса.</summary>
    public string? Name { get; private set; }

    public ProjectRole? MinRole { get; private set; }

    public bool RequireAssignee { get; private set; }

    public bool RequireChildrenDone { get; private set; }

    public bool RequireChecklistDone { get; private set; }

    private List<Guid> _requireFields = [];

    /// <summary>Id пользовательских полей, которые должны быть заполнены (uuid[] в БД).</summary>
    public IReadOnlyList<Guid> RequireFields => _requireFields;

    public TransitionConditions Conditions => new(MinRole, RequireAssignee, RequireChildrenDone, RequireChecklistDone, _requireFields);

    private StatusTransition()
    {
        // EF Core
    }

    internal StatusTransition(Guid boardId, Guid? fromStatusId, Guid toStatusId, string? name, TransitionConditions conditions, Guid? taskTypeId = null)
    {
        Id = Guid.NewGuid();
        BoardId = boardId;
        TaskTypeId = taskTypeId;
        FromStatusId = fromStatusId;
        ToStatusId = toStatusId;
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim().Length <= NameMaxLength
            ? name.Trim()
            : throw new ArgumentException($"Transition name must be at most {NameMaxLength} characters.", nameof(name));
        if (conditions.MinRole is { } role && !Enum.IsDefined(role))
            throw new ArgumentException($"Unknown role {role}.", nameof(conditions));
        MinRole = conditions.MinRole;
        RequireAssignee = conditions.RequireAssignee;
        RequireChildrenDone = conditions.RequireChildrenDone;
        RequireChecklistDone = conditions.RequireChecklistDone;
        _requireFields = conditions.RequireFields?.Distinct().ToList() ?? [];
    }
}

/// <summary>Переход, как его описывает редактор: без Id — workflow заменяется целиком.</summary>
public sealed record TransitionSpec(Guid? FromStatusId, Guid ToStatusId, string? Name = null, TransitionConditions? Conditions = null);

/// <summary>
/// Что знает о задаче и человеке проверка перехода — это приносит хендлер: у домена нет доступа к пользователям
/// и соседним задачам (тот же принцип, что с активностью исполнителя).
/// FilledFields — Id пользовательских полей с непустым значением (для RequireFields); null — не проверялось.
/// </summary>
public sealed record TransitionContext(ProjectRole ActorRole, bool HasAssignee, bool ChildrenDone, bool ChecklistDone, IReadOnlySet<Guid>? FilledFields = null);

/// <summary>Результат проверки: разрешён ли переход и, если нет, почему — по-русски, для подсказки человеку.</summary>
public sealed record TransitionCheck(bool Allowed, IReadOnlyList<string> Reasons)
{
    public static readonly TransitionCheck Ok = new(true, []);

    public static TransitionCheck Denied(params string[] reasons) => new(false, reasons);
}
