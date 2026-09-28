using System.Text.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.CustomFields;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Client.Services;

/// <summary>
/// Раскладка экрана задачи (docs/TZ_workflow_config.md §3) — то же правило, что Board.ResolveScreen на сервере:
/// экран типа → экран «для всех типов» → встроенный (все поля в прежнем порядке). Клиент считает её сам из
/// BoardResponse.Screens, поэтому отдельного запроса за раскладкой нет. Название и статус есть всегда.
/// </summary>
public static class ScreenLayout
{
    public const string System = "system:";
    public const string Custom = "custom:";

    /// <summary>Порядок встроенного экрана (Domain.ScreenFields.System).</summary>
    public static readonly IReadOnlyList<string> SystemFields =
        ["type", "parent", "sprint", "milestone", "priority", "assignee", "team", "start", "due", "estimate", "description", "checklist", "links", "attachments"];

    /// <summary>Что заполняется в форме создания (Domain.ScreenFields.OnCreate).</summary>
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

    public static bool IsConfigured(BoardResponse board, Guid? typeId, ScreenContext context) =>
        (board.Screens ?? []).Any(s => s.TaskTypeId == typeId && s.Context == context);

    public static IReadOnlyList<ScreenFieldDto> Resolve(BoardResponse board, Guid typeId, ScreenContext context,
        IReadOnlyDictionary<Guid, JsonElement>? values = null)
    {
        var screens = board.Screens ?? [];
        var screen = screens.FirstOrDefault(s => s.TaskTypeId == typeId && s.Context == context)
                     ?? screens.FirstOrDefault(s => s.TaskTypeId is null && s.Context == context);
        return screen?.Fields ?? Default(board, typeId, context, values);
    }

    /// <summary>Встроенный экран; архивное поле с уже записанным значением тоже в нём — только для чтения.</summary>
    public static IReadOnlyList<ScreenFieldDto> Default(BoardResponse board, Guid typeId, ScreenContext context,
        IReadOnlyDictionary<Guid, JsonElement>? values = null) =>
        SystemFields.Where(n => context == ScreenContext.Detail || OnCreate.Contains(n))
            .Select(n => new ScreenFieldDto(System + n))
            .Concat(CustomFieldMeta.ForTask(board, typeId, values).Select(f => new ScreenFieldDto(Custom + f.Id, f.IsRequired)))
            .ToList();

    public static string? SystemName(ScreenFieldDto field) =>
        field.Field.StartsWith(System, StringComparison.Ordinal) ? field.Field[System.Length..] : null;

    public static Guid? CustomId(ScreenFieldDto field) =>
        field.Field.StartsWith(Custom, StringComparison.Ordinal) && Guid.TryParse(field.Field[Custom.Length..], out var id) ? id : null;

    public static CustomFieldResponse? CustomField(BoardResponse board, ScreenFieldDto field) =>
        CustomId(field) is { } id ? board.CustomFields?.FirstOrDefault(f => f.Id == id) : null;

    public static string FieldLabel(BoardResponse board, ScreenFieldDto field) =>
        SystemName(field) is { } name ? Label(name) : CustomField(board, field)?.Name ?? "(удалённое поле)";

    /// <summary>Пусто ли системное поле у задачи — для подсветки «обязательного» на экране карточки.</summary>
    public static bool IsEmpty(string name, TaskResponse task) => name switch
    {
        "parent" => task.ParentId is null,
        "sprint" => task.SprintId is null,
        "milestone" => task.MilestoneId is null,
        "priority" => task.Priority == TaskPriority.None,
        "assignee" => task.AssigneeId is null,
        "team" => task.TeamId is null,
        "start" => task.StartDate is null,
        "due" => task.DueDate is null,
        "estimate" => task.StoryPoints is null && task.EstimateMinutes is null,
        "description" => string.IsNullOrWhiteSpace(task.Description),
        "checklist" => task.ChecklistTotal == 0,
        _ => false
    };
}
