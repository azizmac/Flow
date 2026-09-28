using System.Text.Json;

namespace Flow.Shared.Contracts.Tasks;

/// <summary>Подзадача шаблона: TypeId = null — подходящий тип уровнем ниже задачи.</summary>
public sealed record TaskTemplateSubtaskDto(string Title, Guid? TypeId = null, IReadOnlyList<string>? Checklist = null);

/// <summary>
/// Шаблон задачи (docs/TZ_workflow_config.md §5, этап 3G). RenderedTitle — название по образцу на сегодня
/// ({date}, {n}); им форма создания заполняет поле «Название».
/// </summary>
public sealed record TaskTemplateResponse(
    Guid Id,
    Guid BoardId,
    string Name,
    string TitlePattern,
    string RenderedTitle,
    Guid? TypeId,
    TaskPriority Priority,
    string? Description,
    IReadOnlyDictionary<Guid, JsonElement> CustomFields,
    IReadOnlyList<string> Checklist,
    IReadOnlyList<TaskTemplateSubtaskDto> Subtasks,
    Guid CreatedById,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int UsageCount);

/// <summary>Шаблон целиком (создание и правка — полная замена).</summary>
public sealed record SaveTaskTemplateRequest(
    string Name,
    string TitlePattern,
    Guid? TypeId = null,
    TaskPriority Priority = TaskPriority.None,
    string? Description = null,
    IReadOnlyDictionary<Guid, JsonElement?>? CustomFields = null,
    IReadOnlyList<string>? Checklist = null,
    IReadOnlyList<TaskTemplateSubtaskDto>? Subtasks = null);

/// <summary>«Сохранить задачу как шаблон»: название, тип, приоритет, описание, поля, чек-лист и прямые подзадачи.</summary>
public sealed record TaskTemplateFromTaskRequest(string Name);
