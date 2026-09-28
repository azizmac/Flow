namespace Flow.Domain.Entities;

/// <summary>Где показывается экран: форма создания или карточка задачи (docs/TZ_workflow_config.md §3).</summary>
public enum ScreenContext
{
    Create = 0,
    Detail = 1
}

/// <summary>
/// Поле на экране: Field — «system:&lt;имя&gt;» или «custom:&lt;Id поля&gt;», Required — на Create обязательное (проверяет
/// сервер), на Detail — только подсветка незаполненного; Section — заголовок группы, с которого начинается поле.
/// </summary>
public sealed class ScreenField
{
    public const int SectionMaxLength = 40;

    public string Field { get; private set; } = string.Empty;

    public bool Required { get; private set; }

    public string? Section { get; private set; }

    private ScreenField()
    {
        // EF Core (JSON)
    }

    public ScreenField(string field, bool required = false, string? section = null)
    {
        Field = field?.Trim() ?? string.Empty;
        Required = required;
        var trimmed = section?.Trim();
        Section = string.IsNullOrEmpty(trimmed) ? null
            : trimmed.Length <= SectionMaxLength ? trimmed
            : throw new ArgumentException($"Section title must be at most {SectionMaxLength} characters.", nameof(section));
    }
}

/// <summary>
/// Экран задачи — какие поля и в каком порядке видны (docs/TZ_workflow_config.md §3). Часть агрегата Board:
/// TaskTypeId = null — для всех типов. Проект без экранов выглядит как раньше: срабатывает встроенный экран
/// (<see cref="ScreenFields.Default"/>). Скрытое поле значение не теряет — экран только про показ.
/// </summary>
public sealed class TaskScreen
{
    private List<ScreenField> _fields = [];

    public Guid Id { get; private set; }

    public Guid BoardId { get; private set; }

    public Guid? TaskTypeId { get; private set; }

    public ScreenContext Context { get; private set; }

    public IReadOnlyList<ScreenField> Fields => _fields;

    private TaskScreen()
    {
        // EF Core
    }

    internal TaskScreen(Guid boardId, Guid? taskTypeId, ScreenContext context)
    {
        Id = Guid.NewGuid();
        BoardId = boardId;
        TaskTypeId = taskTypeId;
        Context = context;
    }

    internal void SetFields(IEnumerable<ScreenField> fields) => _fields = fields.ToList();
}

/// <summary>Системные поля экрана: имена, подписи и что можно вынести на форму создания.</summary>
public static class ScreenFields
{
    public const string SystemPrefix = "system:";
    public const string CustomPrefix = "custom:";

    /// <summary>Порядок встроенного экрана — тот же, что был в карточке до экранов.</summary>
    public static readonly IReadOnlyList<string> System =
        ["type", "parent", "sprint", "milestone", "priority", "assignee", "team", "start", "due", "estimate", "description", "checklist", "links", "attachments"];

    /// <summary>
    /// Что заполняется в форме создания. Остальное (даты, оценки, спринт, связи, файлы) ставится уже у созданной
    /// задачи — такие поля на экран Create не выносятся.
    /// </summary>
    public static readonly IReadOnlySet<string> OnCreate = new HashSet<string> { "type", "priority", "assignee", "description" };

    public static string Label(string name) => name switch
    {
        "type" => "Тип",
        "parent" => "Родитель",
        "sprint" => "Спринт",
        "milestone" => "Веха",
        "priority" => "Приоритет",
        "assignee" => "Исполнитель",
        "team" => "Команда",
        "start" => "Начало",
        "due" => "Срок",
        "estimate" => "Оценка",
        "description" => "Описание",
        "checklist" => "Чек-лист",
        "links" => "Связи",
        "attachments" => "Вложения",
        _ => name
    };

    /// <summary>Встроенный экран: системные поля (на Create — только допустимые там) и все неархивные пользовательские поля типа.</summary>
    public static IReadOnlyList<ScreenField> Default(Board board, Guid typeId, ScreenContext context) =>
        System.Where(n => context == ScreenContext.Detail || OnCreate.Contains(n))
            .Select(n => new ScreenField(SystemPrefix + n))
            .Concat(board.CustomFields.Where(f => f.AppliesTo(typeId)).OrderBy(f => f.SortOrder)
                .Select(f => new ScreenField(CustomPrefix + f.Id, f.IsRequired)))
            .ToList();
}
