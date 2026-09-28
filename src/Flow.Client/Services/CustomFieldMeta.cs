using System.Globalization;
using System.Text.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.CustomFields;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Client.Services;

/// <summary>
/// Подписи и отображение пользовательских полей (docs/TZ_task_model.md §4): названия типов, какие поля показывать
/// задаче, значение по-русски. Форма JSON — как в CustomFieldValidator: строка, число, массив Id, true/false.
/// </summary>
public static class CustomFieldMeta
{
    public static readonly IReadOnlyList<CustomFieldType> Types = Enum.GetValues<CustomFieldType>();

    public static string TypeLabel(CustomFieldType type) => type switch
    {
        CustomFieldType.Text => "Строка",
        CustomFieldType.LongText => "Текст",
        CustomFieldType.Number => "Число",
        CustomFieldType.Date => "Дата",
        CustomFieldType.Select => "Список",
        CustomFieldType.MultiSelect => "Несколько из списка",
        CustomFieldType.User => "Человек",
        CustomFieldType.Checkbox => "Флажок",
        CustomFieldType.Url => "Ссылка",
        _ => type.ToString()
    };

    public static bool HasOptions(CustomFieldType type) => type is CustomFieldType.Select or CustomFieldType.MultiSelect;

    /// <summary>Поля задачи: не в архиве и подходящие к её типу; архивное с уже записанным значением тоже видно (только чтение).</summary>
    public static IEnumerable<CustomFieldResponse> ForTask(BoardResponse? board, Guid typeId, IReadOnlyDictionary<Guid, JsonElement>? values) =>
        (board?.CustomFields ?? [])
            .Where(f => (!f.IsArchived && (f.TaskTypeIds.Count == 0 || f.TaskTypeIds.Contains(typeId)))
                        || (values?.ContainsKey(f.Id) ?? false))
            .OrderBy(f => f.SortOrder);

    /// <summary>Значение для чтения; удалённый вариант — «(удалённое значение)», он очистится при следующей правке.</summary>
    public static string Display(CustomFieldResponse field, JsonElement value, Func<Guid, string?> userName) => field.Type switch
    {
        CustomFieldType.Number => value.TryGetDecimal(out var n) ? n.ToString("0.##", CultureInfo.InvariantCulture) : "—",
        CustomFieldType.Date => DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? Ru.DateOnlyShort(d) : "—",
        CustomFieldType.Checkbox => value.ValueKind == JsonValueKind.True ? "да" : "нет",
        CustomFieldType.Select => OptionLabel(field, value.GetString()),
        CustomFieldType.MultiSelect => value.ValueKind == JsonValueKind.Array
            ? string.Join(", ", value.EnumerateArray().Select(e => OptionLabel(field, e.GetString())))
            : "—",
        CustomFieldType.User => Guid.TryParse(value.GetString(), out var id) ? userName(id) ?? "(удалённый человек)" : "—",
        _ => value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText()
    };

    private static string OptionLabel(CustomFieldResponse field, string? id) =>
        Guid.TryParse(id, out var optionId) && field.Options.FirstOrDefault(o => o.Id == optionId) is { } option
            ? option.Label
            : "(удалённое значение)";

    public static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
}
