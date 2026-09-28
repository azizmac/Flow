namespace Flow.Shared.Contracts.Boards;

/// <summary>Шаблон проекта (docs/TZ_workflow_config.md §4): встроенный (в коде) или сохранённый из проекта.</summary>
public sealed record BoardTemplateResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsBuiltIn,
    Guid? CreatedById,
    DateTime? CreatedAt,
    WorkflowMode WorkflowMode,
    IReadOnlyList<string> Statuses,
    IReadOnlyList<string> TaskTypes,
    IReadOnlyList<string> CustomFields,
    int SampleTaskCount);

/// <summary>«Сохранить проект как шаблон»: IncludeTasks — до 50 задач верхнего уровня как образец.</summary>
public sealed record SaveBoardTemplateRequest(string Name, string? Description, bool IncludeTasks);

/// <summary>Что переносится из проекта в проекты (флаги).</summary>
[Flags]
public enum ConfigParts
{
    None = 0,
    Statuses = 1,
    Workflow = 2,
    TaskTypes = 4,
    CustomFields = 8,
    Screens = 16,
    All = Statuses | Workflow | TaskTypes | CustomFields | Screens
}

/// <summary>Целевой проект и карта его статусов: статус цели → статус источника (не указан — предложение сервера).</summary>
public sealed record ApplyConfigTarget(Guid BoardId, IReadOnlyDictionary<Guid, Guid>? StatusMap = null);

public sealed record ApplyBoardConfigRequest(IReadOnlyList<ApplyConfigTarget> Targets, ConfigParts Parts);

public sealed record ConfigSourceStatus(Guid Id, string Name, bool IsInitial, bool IsFinal);

/// <summary>Статус цели и во что он превратится: MapsTo — статус источника; Kept — он сам им станет, иначе удалится с переносом задач.</summary>
public sealed record ConfigStatusMapping(Guid StatusId, string Name, int TaskCount, Guid MapsTo, bool Kept);

public sealed record ConfigTargetPreview(
    Guid BoardId,
    string Key,
    string Name,
    IReadOnlyList<ConfigStatusMapping> StatusMap,
    IReadOnlyList<string> StatusesCreated,
    IReadOnlyList<string> StatusesRenamed,
    IReadOnlyList<string> StatusesRemoved,
    int TasksMoved,
    IReadOnlyList<string> TaskTypesAdded,
    IReadOnlyList<string> CustomFieldsAdded,
    IReadOnlyList<string> CustomFieldsUpdated,
    string? Error);

public sealed record ApplyConfigPreviewResponse(Guid SourceBoardId, IReadOnlyList<ConfigSourceStatus> SourceStatuses, IReadOnlyList<ConfigTargetPreview> Targets);

/// <summary>Итог по проекту: ошибка одного не откатывает остальные; Changed = false — повтор без изменений в источнике.</summary>
public sealed record ApplyConfigTargetResult(Guid BoardId, bool Success, bool Changed, int TasksMoved, string? Error);

public sealed record ApplyBoardConfigResponse(IReadOnlyList<ApplyConfigTargetResult> Results);
