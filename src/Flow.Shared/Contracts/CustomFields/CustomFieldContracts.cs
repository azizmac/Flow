using System.Text.Json;

namespace Flow.Shared.Contracts.CustomFields;

/// <summary>Зеркало Domain.CustomFieldType.</summary>
public enum CustomFieldType
{
    Text = 0,
    LongText = 1,
    Number = 2,
    Date = 3,
    Select = 4,
    MultiSelect = 5,
    User = 6,
    Checkbox = 7,
    Url = 8
}

public sealed record CustomFieldOptionResponse(Guid Id, string Label, string? Color);

/// <summary>Пользовательское поле проекта (docs/TZ_task_model.md §4); TaskTypeIds пусто — поле у всех типов.</summary>
public sealed record CustomFieldResponse(
    Guid Id,
    string Key,
    string Name,
    CustomFieldType Type,
    IReadOnlyList<CustomFieldOptionResponse> Options,
    bool IsRequired,
    IReadOnlyList<Guid> TaskTypeIds,
    int SortOrder,
    bool IsArchived);

/// <summary>POST /boards/{id}/custom-fields: Options — подписи вариантов (только Select/MultiSelect).</summary>
public sealed record CreateCustomFieldRequest(
    string Key,
    string Name,
    CustomFieldType Type,
    IReadOnlyList<string>? Options = null,
    bool IsRequired = false,
    IReadOnlyList<Guid>? TaskTypeIds = null);

/// <summary>Вариант при правке: Id — существующий (переименование), null — новый; пропавшие удаляются.</summary>
public sealed record CustomFieldOptionRequest(Guid? Id, string Label, string? Color = null);

/// <summary>PATCH /boards/{id}/custom-fields/{fieldId}: null — не трогать. Ключ и тип не меняются.</summary>
public sealed record UpdateCustomFieldRequest(
    string? Name = null,
    IReadOnlyList<CustomFieldOptionRequest>? Options = null,
    bool? IsRequired = null,
    IReadOnlyList<Guid>? TaskTypeIds = null,
    bool? IsArchived = null);

/// <summary>PUT /boards/{id}/custom-fields/order — все поля проекта в новом порядке.</summary>
public sealed record ReorderCustomFieldsRequest(IReadOnlyList<Guid> FieldIds);

/// <summary>
/// PATCH /tasks/{id}/custom-fields: PATCH-семантика по полям — перечисленные меняются, null очищает.
/// Форма значения по типу: строка (Text, LongText, Url, Date «yyyy-MM-dd», Select и User — Id), число, массив Id
/// (MultiSelect), true/false (Checkbox).
/// </summary>
public sealed record SetCustomFieldsRequest(IReadOnlyDictionary<Guid, JsonElement?> Values);
