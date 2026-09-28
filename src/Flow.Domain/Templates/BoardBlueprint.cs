using Flow.Domain.Entities;

namespace Flow.Domain.Templates;

/// <summary>
/// Конфигурация проекта без задач и без Id (docs/TZ_workflow_config.md §4, этап 3F) — то, из чего собирается проект по
/// шаблону и что переносится «применить конфигурацию проекта A к проектам B, C». Статусы, типы и поля связаны
/// локальными ключами чертежа (у снимка — Id исходного проекта строкой, у встроенных шаблонов — читаемые слова),
/// поэтому чертёж переносим между проектами. Живой общей ссылки нет: статусы остаются частью агрегата Board.
/// </summary>
public sealed record BoardBlueprint(
    IReadOnlyList<BlueprintStatus> Statuses,
    WorkflowMode WorkflowMode,
    IReadOnlyList<BlueprintTransition> Transitions,
    IReadOnlyList<BlueprintTaskType> TaskTypes,
    IReadOnlyList<BlueprintField> CustomFields,
    IReadOnlyList<BlueprintScreen> Screens)
{
    public const int CurrentVersion = 1;

    public const int MaxSampleTasks = 50;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Образцы задач верхнего уровня (флажок «с задачами» при сохранении шаблона) — создаются в новом проекте.</summary>
    public IReadOnlyList<BlueprintSampleTask> SampleTasks { get; init; } = [];

    /// <summary>Инварианты статусов чертежа — те же, что у проекта: ровно один начальный (не финальный), хотя бы один финальный.</summary>
    public void Validate()
    {
        if (Statuses.Count == 0)
            throw new ArgumentException("В шаблоне нет статусов.");
        if (Statuses.Count(s => s.IsInitial) != 1)
            throw new ArgumentException("В шаблоне должен быть ровно один начальный статус.");
        if (Statuses.Any(s => s.IsInitial && s.IsFinal))
            throw new ArgumentException("Начальный статус шаблона не может быть финальным.");
        if (!Statuses.Any(s => s.IsFinal))
            throw new ArgumentException("В шаблоне нужен хотя бы один финальный статус.");
        if (Statuses.GroupBy(s => s.Key).Any(g => g.Count() > 1) || TaskTypes.GroupBy(t => t.Key).Any(g => g.Count() > 1)
            || CustomFields.GroupBy(f => f.Key).Any(g => g.Count() > 1))
            throw new ArgumentException("Ключи статусов, типов и полей шаблона повторяются.");
        if (TaskTypes.Count > 0 && TaskTypes.Count(t => t.IsDefault) != 1)
            throw new ArgumentException("В шаблоне должен быть ровно один тип задач по умолчанию.");
        if (SampleTasks.Count > MaxSampleTasks)
            throw new ArgumentException($"В шаблоне не больше {MaxSampleTasks} задач-образцов.");
    }
}

/// <summary>Задача-образец: название, описание и тип ключом чертежа (null — тип по умолчанию).</summary>
public sealed record BlueprintSampleTask(string Title, string? Description = null, string? TypeKey = null);

public sealed record BlueprintStatus(string Key, string Name, StatusType? Type, bool IsInitial, bool IsFinal, int? WipLimit = null);

/// <summary>Переход; From = null — «из любого»; TypeKey — переход своего workflow типа (этап 3E), null — проекта.</summary>
public sealed record BlueprintTransition(
    string? From,
    string To,
    string? Name = null,
    ProjectRole? MinRole = null,
    bool RequireAssignee = false,
    bool RequireChildrenDone = false,
    bool RequireChecklistDone = false,
    IReadOnlyList<string>? RequireFieldKeys = null,
    string? TypeKey = null);

/// <summary>Тип задачи; OwnWorkflowMode — у типа свой workflow (его переходы — с TypeKey этого типа).</summary>
public sealed record BlueprintTaskType(string Key, string Name, TaskTypeKind Kind, bool IsDefault, WorkflowMode? OwnWorkflowMode = null);

/// <summary>Пользовательское поле: ключ поля проекта, варианты — подписями, типы задач — ключами чертежа (пусто — все).</summary>
public sealed record BlueprintField(
    string Key, string Name, CustomFieldType Type, IReadOnlyList<string>? Options = null, bool IsRequired = false, IReadOnlyList<string>? TypeKeys = null);

/// <summary>Экран: TypeKey null — для всех типов. Поле — «system:имя» или «custom:ключ поля» (не Id: Id у каждого проекта свой).</summary>
public sealed record BlueprintScreen(string? TypeKey, ScreenContext Context, IReadOnlyList<BlueprintScreenField> Fields);

public sealed record BlueprintScreenField(string Field, bool Required = false, string? Section = null);

/// <summary>Что переносится (флаги): статусы, workflow, типы, поля, экраны.</summary>
[Flags]
public enum BlueprintParts
{
    None = 0,
    Statuses = 1,
    Workflow = 2,
    Types = 4,
    Fields = 8,
    Screens = 16,
    All = Statuses | Workflow | Types | Fields | Screens
}

/// <summary>Итог шага статусов: ключ статуса чертежа → статус проекта; лишние статусы и куда переносить их задачи.</summary>
public sealed record BlueprintStatusResult(IReadOnlyDictionary<string, Guid> StatusIds, IReadOnlyList<(Guid StatusId, Guid MoveTo)> Removals);
