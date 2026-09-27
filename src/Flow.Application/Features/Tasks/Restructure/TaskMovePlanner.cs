using System.Text.Json.Nodes;
using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Application.Features.Tasks.Restructure;

/// <summary>
/// План переноса задачи в другой проект (docs/TZ_task_model.md §6) — общий для превью и самой команды, чтобы
/// диалог показывал ровно то, что сделает сервер. Чистый расчёт по уже загруженным проектам и задачам:
/// статус — карта → тот же вид → начальный; тип — карта → тот же вид → тип по умолчанию; значение поля — поле
/// с тем же ключом и типом (варианты — по подписи), иначе потеря. Подзадача должна остаться ниже родителя по
/// уровню типа — иначе проблема, и перенос откажет до любых изменений.
/// </summary>
internal static class TaskMovePlanner
{
    public sealed record Item(TaskItem Task, Guid StatusId, Guid TypeId, string CustomFieldsJson, bool KeepParent);

    public sealed record Plan(
        IReadOnlyList<Item> Items,
        IReadOnlyList<MoveMapping> Statuses,
        IReadOnlyList<MoveMapping> Types,
        IReadOnlyList<LostFieldValue> LostFields,
        IReadOnlyList<string> Problems,
        bool ParentDropped);

    /// <summary>Поддерево в порядке обхода в ширину: корень первым, родитель раньше детей.</summary>
    public static async Task<IReadOnlyList<TaskItem>> SubtreeAsync(ITaskItemRepository tasks, TaskItem root, CancellationToken cancellationToken)
    {
        var subtree = new List<TaskItem> { root };
        for (var i = 0; i < subtree.Count; i++)
            subtree.AddRange(await tasks.GetChildrenAsync(subtree[i].Id, cancellationToken));
        return subtree;
    }

    public static Plan Build(Board source, Board target, IReadOnlyList<TaskItem> subtree,
        IReadOnlyDictionary<Guid, Guid>? statusMap, IReadOnlyDictionary<Guid, Guid>? typeMap)
    {
        var problems = new List<string>();
        var lost = new List<LostFieldValue>();
        var items = new List<Item>();
        var ids = subtree.Select(t => t.Id).ToHashSet();
        var levels = new Dictionary<Guid, int>();

        foreach (var task in subtree)
        {
            var statusId = MapStatus(source, target, task.StatusId, statusMap, problems);
            var type = MapType(source, target, task.TypeId, typeMap, problems);
            var keepParent = task.ParentId is { } p && ids.Contains(p);

            // Уровень считается по уже выбранным типам: родитель в поддереве обработан раньше (обход в ширину).
            if (keepParent && levels.TryGetValue(task.ParentId!.Value, out var parentLevel) && type.Level <= parentLevel)
                problems.Add($"{task.Code.Value}: тип «{type.Name}» в проекте {target.Key} не ниже типа родителя — выберите другой тип для «{source.GetTaskType(task.TypeId).Name}».");
            levels[task.Id] = type.Level;

            items.Add(new Item(task, statusId, type.Id, MapFields(source, target, task, type.Id, lost), keepParent));
        }

        MoveMapping Mapping(Guid from, string fromName, Guid to, string toName, int count) => new(from, fromName, to, toName, count);
        var statuses = items.GroupBy(i => (From: i.Task.StatusId, To: i.StatusId))
            .Select(g => Mapping(g.Key.From, source.Statuses.First(s => s.Id == g.Key.From).Name, g.Key.To, target.Statuses.First(s => s.Id == g.Key.To).Name, g.Count()))
            .ToList();
        var types = items.GroupBy(i => (From: i.Task.TypeId, To: i.TypeId))
            .Select(g => Mapping(g.Key.From, source.GetTaskType(g.Key.From).Name, g.Key.To, target.GetTaskType(g.Key.To).Name, g.Count()))
            .ToList();

        return new Plan(items, statuses, types, lost, problems.Distinct().ToList(), subtree[0].ParentId is not null);
    }

    private static Guid MapStatus(Board source, Board target, Guid statusId, IReadOnlyDictionary<Guid, Guid>? map, List<string> problems)
    {
        if (map is not null && map.TryGetValue(statusId, out var mapped))
        {
            if (target.Statuses.Any(s => s.Id == mapped))
                return mapped;
            problems.Add($"Статус {mapped} не из проекта {target.Key}.");
        }

        var type = source.Statuses.First(s => s.Id == statusId).Type;
        return (type is null ? null : target.Statuses.Where(s => s.Type == type).OrderBy(s => s.SortOrder).FirstOrDefault())?.Id
               ?? target.Statuses.Single(s => s.IsInitial).Id;
    }

    private static TaskType MapType(Board source, Board target, Guid typeId, IReadOnlyDictionary<Guid, Guid>? map, List<string> problems)
    {
        if (map is not null && map.TryGetValue(typeId, out var mapped))
        {
            var chosen = target.TaskTypes.FirstOrDefault(t => t.Id == mapped);
            if (chosen is { IsArchived: false })
                return chosen;
            problems.Add($"Тип {mapped} не из проекта {target.Key} или в архиве.");
        }

        var kind = source.GetTaskType(typeId).Kind;
        return target.TaskTypes.Where(t => t.Kind == kind && !t.IsArchived).OrderBy(t => t.SortOrder).FirstOrDefault()
               ?? target.TaskTypes.Single(t => t.IsDefault);
    }

    /// <summary>Значения, для которых в целевом проекте есть поле с тем же ключом и типом; варианты — по подписи без учёта регистра.</summary>
    private static string MapFields(Board source, Board target, TaskItem task, Guid targetTypeId, List<LostFieldValue> lost)
    {
        var result = new JsonObject();
        foreach (var (fieldId, value) in task.CustomFieldValues())
        {
            var from = source.CustomFields.FirstOrDefault(f => f.Id == fieldId);
            if (from is null)
                continue;

            var to = target.CustomFields.FirstOrDefault(f => string.Equals(f.Key, from.Key, StringComparison.OrdinalIgnoreCase)
                                                             && f.Type == from.Type && f.AppliesTo(targetTypeId));
            JsonNode? mapped = to is null ? null : from.HasOptions ? MapOptions(from, to, value) : JsonNode.Parse(value.GetRawText());
            if (mapped is null || mapped is JsonArray { Count: 0 })
            {
                lost.Add(new LostFieldValue(task.Code.Value, from.Name));
                continue;
            }

            // MultiSelect с частью вариантов переносится частично — остальное тоже потеря.
            if (from.Type == CustomFieldType.MultiSelect && mapped is JsonArray array && array.Count < value.GetArrayLength())
                lost.Add(new LostFieldValue(task.Code.Value, from.Name));
            result[to!.Id.ToString()] = mapped;
        }

        return result.ToJsonString(CustomFieldValidator.JsonOptions);
    }

    private static JsonNode? MapOptions(CustomFieldDefinition from, CustomFieldDefinition to, System.Text.Json.JsonElement value)
    {
        Guid? Map(string? optionId) =>
            Guid.TryParse(optionId, out var id) && from.FindOption(id) is { } option
                ? to.Options.FirstOrDefault(o => string.Equals(o.Label, option.Label, StringComparison.OrdinalIgnoreCase))?.Id
                : null;

        if (value.ValueKind == System.Text.Json.JsonValueKind.Array)
            return new JsonArray(value.EnumerateArray().Select(e => Map(e.GetString())).OfType<Guid>().Select(id => (JsonNode)JsonValue.Create(id.ToString())!).ToArray());

        return Map(value.GetString()) is { } single ? JsonValue.Create(single.ToString()) : null;
    }
}
