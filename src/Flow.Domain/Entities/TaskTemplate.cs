using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flow.Domain.Entities;

/// <summary>Подзадача шаблона: название, тип (null — подходящий тип уровнем ниже) и пункты чек-листа.</summary>
public sealed record TaskTemplateSubtask(string Title, Guid? TypeId, IReadOnlyList<string> Checklist);

/// <summary>
/// Шаблон задачи проекта (docs/TZ_workflow_config.md §5, этап 3G): чем заполнить форму создания — название по образцу
/// (<c>{date}</c> — сегодняшняя дата, <c>{n}</c> — номер использования), тип, приоритет, описание, значения полей — и
/// что создать вместе с задачей: чек-лист и подзадачи. Отдельный агрегат, как спринт и веха: у проекта их может быть
/// много, а грузить их вместе с доской незачем. Тип и поля проверяет Application: им нужен проект.
/// </summary>
public sealed class TaskTemplate
{
    public const int NameMaxLength = 80;
    public const int TitleMaxLength = 500;
    public const int DescriptionMaxLength = 20000;
    public const int MaxSubtasks = 20;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Guid Id { get; private set; }

    public Guid BoardId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Guid? TypeId { get; private set; }

    public string TitlePattern { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public TaskPriority Priority { get; private set; }

    /// <summary>Значения пользовательских полей — объект «Id поля → значение», как <see cref="TaskItem.CustomFieldsJson"/>.</summary>
    public string CustomFieldsJson { get; private set; } = "{}";

    private List<string> _checklist = [];

    public IReadOnlyList<string> Checklist => _checklist;

    /// <summary>Подзадачи — jsonb: вложенный чек-лист у каждой, своих Id и ссылок на них нет.</summary>
    public string SubtasksJson { get; private set; } = "[]";

    public IReadOnlyList<TaskTemplateSubtask> Subtasks =>
        JsonSerializer.Deserialize<List<TaskTemplateSubtask>>(SubtasksJson, Json) ?? [];

    public Guid CreatedById { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public int SortOrder { get; private set; }

    /// <summary>Сколько задач создано по шаблону — из него берётся <c>{n}</c>.</summary>
    public int UsageCount { get; private set; }

    private TaskTemplate()
    {
        // EF Core
    }

    public static TaskTemplate Create(Guid boardId, string name, string titlePattern, Guid createdById, int sortOrder)
    {
        if (boardId == Guid.Empty)
            throw new ArgumentException("Board id must not be empty.", nameof(boardId));
        if (createdById == Guid.Empty)
            throw new ArgumentException("Creator id must not be empty.", nameof(createdById));

        var now = DateTime.UtcNow;
        var template = new TaskTemplate
        {
            Id = Guid.NewGuid(), BoardId = boardId, CreatedById = createdById, CreatedAt = now, UpdatedAt = now, SortOrder = sortOrder
        };
        template.Rename(name);
        template.SetTitlePattern(titlePattern);
        return template;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Название шаблона не может быть пустым.", nameof(name));
        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Название шаблона — не длиннее {NameMaxLength} символов.", nameof(name));
        Name = trimmed;
        Touch();
    }

    public void SetTitlePattern(string titlePattern)
    {
        if (string.IsNullOrWhiteSpace(titlePattern))
            throw new ArgumentException("Название задачи в шаблоне не может быть пустым.", nameof(titlePattern));
        var trimmed = titlePattern.Trim();
        if (trimmed.Length > TitleMaxLength)
            throw new ArgumentException($"Название задачи в шаблоне — не длиннее {TitleMaxLength} символов.", nameof(titlePattern));
        TitlePattern = trimmed;
        Touch();
    }

    /// <summary>Тип, приоритет и описание; тип из того же проекта и не архивный — проверяет Application.</summary>
    public void SetDefaults(Guid? typeId, TaskPriority priority, string? description)
    {
        if (!Enum.IsDefined(priority))
            throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown task priority.");
        var text = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (text?.Length > DescriptionMaxLength)
            throw new ArgumentException($"Описание шаблона — не длиннее {DescriptionMaxLength} символов.", nameof(description));

        TypeId = typeId;
        Priority = priority;
        Description = text;
        Touch();
    }

    /// <summary>Значения полей, уже нормализованные CustomFieldValidator'ом; пустые не хранятся.</summary>
    public void SetCustomFields(IReadOnlyDictionary<Guid, JsonNode> values)
    {
        var obj = new JsonObject();
        foreach (var (fieldId, value) in values)
            obj[fieldId.ToString()] = value.DeepClone();
        CustomFieldsJson = obj.ToJsonString(CustomFieldValidator.JsonOptions);
        Touch();
    }

    public void SetChecklist(IEnumerable<string> items)
    {
        _checklist = ValidateChecklist(items);
        Touch();
    }

    public void SetSubtasks(IEnumerable<TaskTemplateSubtask> subtasks)
    {
        var list = subtasks.Select(s => new TaskTemplateSubtask(
            string.IsNullOrWhiteSpace(s.Title)
                ? throw new ArgumentException("У подзадачи шаблона нет названия.", nameof(subtasks))
                : s.Title.Trim().Length > TitleMaxLength
                    ? throw new ArgumentException($"Название подзадачи — не длиннее {TitleMaxLength} символов.", nameof(subtasks))
                    : s.Title.Trim(),
            s.TypeId,
            ValidateChecklist(s.Checklist ?? []))).ToList();
        if (list.Count > MaxSubtasks)
            throw new ArgumentException($"В шаблоне не больше {MaxSubtasks} подзадач.", nameof(subtasks));

        SubtasksJson = JsonSerializer.Serialize(list, Json);
        Touch();
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    /// <summary>Название задачи по образцу: <c>{date}</c> — дата дд.мм.гггг, <c>{n}</c> — номер следующего использования.</summary>
    public string RenderTitle(DateOnly today) =>
        TitlePattern
            .Replace("{date}", today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{n}", (UsageCount + 1).ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

    /// <summary>По шаблону создана задача — следующий <c>{n}</c> на единицу больше.</summary>
    public void MarkUsed() => UsageCount++;

    private void Touch() => UpdatedAt = DateTime.UtcNow;

    private static List<string> ValidateChecklist(IEnumerable<string> items)
    {
        var list = items.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).ToList();
        if (list.Count > TaskItem.MaxChecklistItems)
            throw new ArgumentException($"В чек-листе шаблона не больше {TaskItem.MaxChecklistItems} пунктов.");
        if (list.Any(i => i.Length > TaskChecklistItem.TextMaxLength))
            throw new ArgumentException($"Пункт чек-листа — не длиннее {TaskChecklistItem.TextMaxLength} символов.");
        return list;
    }
}
