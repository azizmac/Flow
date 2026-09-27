using System.Text.RegularExpressions;

namespace Flow.Domain.Entities;

/// <summary>Тип пользовательского поля (docs/TZ_task_model.md §4); числа хранятся в БД — только дописывать.</summary>
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

/// <summary>Вариант списка у Select/MultiSelect. Значение задачи хранит Id варианта — переименование его не трогает.</summary>
public sealed class CustomFieldOption
{
    public const int LabelMaxLength = 60;

    public Guid Id { get; private set; }

    public string Label { get; private set; } = string.Empty;

    /// <summary>Цвет чипа (#RRGGBB) или null — нейтральный.</summary>
    public string? Color { get; private set; }

    private CustomFieldOption()
    {
        // EF Core (JSON)
    }

    internal CustomFieldOption(Guid id, string label, string? color)
    {
        Id = id;
        Label = label;
        Color = color;
    }
}

/// <summary>
/// Пользовательское поле проекта (docs/TZ_task_model.md §4) — часть агрегата <see cref="Board"/>, как тип задачи.
/// Key — неизменяемое имя для FQL (`cf.sla_level`), значения задач ключуются Id поля: переименование не трогает задачи.
/// Удаления нет — только архив: значения в задачах остаются, поле перестаёт предлагаться.
/// </summary>
public sealed partial class CustomFieldDefinition
{
    public const int NameMaxLength = 60;
    public const int MaxOptions = 100;

    private List<CustomFieldOption> _options = [];
    private List<Guid> _taskTypeIds = [];

    public Guid Id { get; private set; }

    public Guid BoardId { get; private set; }

    public string Key { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public CustomFieldType Type { get; private set; }

    public IReadOnlyList<CustomFieldOption> Options => _options;

    public bool IsRequired { get; private set; }

    /// <summary>Типы задач, у которых поле есть; пусто — у всех.</summary>
    public IReadOnlyList<Guid> TaskTypeIds => _taskTypeIds;

    public int SortOrder { get; private set; }

    public bool IsArchived { get; private set; }

    public bool HasOptions => Type is CustomFieldType.Select or CustomFieldType.MultiSelect;

    private CustomFieldDefinition()
    {
        // EF Core
    }

    internal CustomFieldDefinition(Guid boardId, string key, string name, CustomFieldType type, int sortOrder)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentException($"Unknown custom field type {type}.", nameof(type));

        Id = Guid.NewGuid();
        BoardId = boardId;
        Key = ValidateKey(key);
        Name = ValidateName(name);
        Type = type;
        SortOrder = sortOrder;
    }

    /// <summary>Есть ли поле у задачи этого типа (архивное — ни у кого).</summary>
    public bool AppliesTo(Guid taskTypeId) => !IsArchived && (_taskTypeIds.Count == 0 || _taskTypeIds.Contains(taskTypeId));

    public CustomFieldOption? FindOption(Guid optionId) => _options.FirstOrDefault(o => o.Id == optionId);

    internal void Rename(string name) => Name = ValidateName(name);

    internal void SetRequired(bool isRequired) => IsRequired = isRequired;

    internal void SetArchived(bool isArchived) => IsArchived = isArchived;

    internal void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    internal void SetTaskTypes(IEnumerable<Guid> taskTypeIds) => _taskTypeIds = taskTypeIds.Distinct().ToList();

    /// <summary>
    /// Полный список вариантов: Id — существующий вариант (переименование), null — новый. Пропавшие варианты удаляются,
    /// задачи со ссылкой на них не трогаются: клиент покажет «(удалённое значение)», следующая правка его очистит.
    /// </summary>
    internal void SetOptions(IEnumerable<(Guid? Id, string Label, string? Color)> options)
    {
        if (!HasOptions)
            throw new InvalidOperationException($"A {Type} field has no options.");

        var result = new List<CustomFieldOption>();
        foreach (var (id, label, color) in options)
        {
            var trimmed = label?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
                throw new ArgumentException("Option label must not be empty.", nameof(options));
            if (trimmed.Length > CustomFieldOption.LabelMaxLength)
                throw new ArgumentException($"Option label must be at most {CustomFieldOption.LabelMaxLength} characters.", nameof(options));
            if (result.Any(o => string.Equals(o.Label, trimmed, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Вариант «{trimmed}» повторяется.");
            if (color is not null && !ColorPattern().IsMatch(color))
                throw new ArgumentException($"Color must be #RRGGBB, got '{color}'.", nameof(options));
            if (id is { } existing && _options.All(o => o.Id != existing))
                throw new InvalidOperationException($"Option {existing} does not belong to field {Key}.");

            result.Add(new CustomFieldOption(id ?? Guid.NewGuid(), trimmed, color));
        }

        if (result.Count == 0)
            throw new InvalidOperationException("У списка должен быть хотя бы один вариант.");
        if (result.Count > MaxOptions)
            throw new InvalidOperationException($"Вариантов не больше {MaxOptions}.");

        _options = result;
    }

    internal static string ValidateKey(string key)
    {
        var trimmed = key?.Trim() ?? string.Empty;
        return KeyPattern().IsMatch(trimmed)
            ? trimmed
            : throw new ArgumentException("Ключ поля — латиница в нижнем регистре, цифры и «_», от 2 до 30 символов, начинается с буквы.", nameof(key));
    }

    internal static string ValidateName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException("Custom field name must not be empty.", nameof(name));
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Custom field name must be at most {NameMaxLength} characters.", nameof(name));
        return trimmed;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{1,29}$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();
}
