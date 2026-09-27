namespace Flow.Application.Features.Tasks.Fql;

/// <summary>
/// Каталог полей и словарей FQL — один на биндер и подсказки (GET /tasks/query/suggest), чтобы подсказка не
/// предлагала того, что биндер не примет. Значения — без учёта регистра; у видов и категорий есть русские синонимы.
/// </summary>
public static class FqlFields
{
    public sealed record Field(string Name, string Hint);

    public static readonly IReadOnlyList<Field> All =
    [
        new("project", "ключ проекта"),
        new("key", "код задачи"),
        new("type", "тип задачи проекта"),
        new("typeKind", "вид: epic, story, task, bug, subtask"),
        new("status", "статус проекта"),
        new("statusCategory", "вид статуса: notStarted, inProgress, inReview, done"),
        new("priority", "none, low, medium, high, critical"),
        new("assignee", "@username, me(), EMPTY"),
        new("creator", "@username, me()"),
        new("parent", "код задачи, childrenOf(КОД), EMPTY"),
        new("created", "дата: 2026-09-01, -7d, today(), startOfWeek()"),
        new("updated", "дата изменения"),
        new("start", "дата начала"),
        new("due", "срок"),
        new("points", "story points"),
        new("estimate", "оценка: 30m, 2h, 1d"),
        new("text", "подстрока в названии, описании, коде: text ~ \"слово\""),
        new("linked", "linkedTo(КОД), blockedBy(КОД), isBlocked(), EMPTY")
    ];

    /// <summary>Поля, которые появятся с будущими этапами: биндер отвечает понятной ошибкой, а не «неизвестное поле».</summary>
    public static readonly IReadOnlyDictionary<string, string> Future = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["sprint"] = "спринты появятся на этапе 2D",
        ["milestone"] = "вехи появятся на этапе 2E"
    };

    public static readonly IReadOnlyDictionary<string, Domain.Entities.TaskTypeKind> Kinds = Map(
        (Domain.Entities.TaskTypeKind.Epic, ["epic", "эпик"]),
        (Domain.Entities.TaskTypeKind.Story, ["story", "история"]),
        (Domain.Entities.TaskTypeKind.Task, ["task", "задача"]),
        (Domain.Entities.TaskTypeKind.Bug, ["bug", "ошибка"]),
        (Domain.Entities.TaskTypeKind.Subtask, ["subtask", "подзадача"]));

    public static readonly IReadOnlyDictionary<string, Domain.Entities.StatusType> StatusTypes = Map(
        (Domain.Entities.StatusType.NotStarted, ["notStarted", "не начата"]),
        (Domain.Entities.StatusType.InProgress, ["inProgress", "в работе"]),
        (Domain.Entities.StatusType.InReview, ["inReview", "на проверке"]),
        (Domain.Entities.StatusType.Done, ["done", "сделана"]));

    public static readonly IReadOnlyDictionary<string, Domain.Entities.TaskPriority> Priorities = Map(
        (Domain.Entities.TaskPriority.None, ["none", "без"]),
        (Domain.Entities.TaskPriority.Low, ["low", "низкий"]),
        (Domain.Entities.TaskPriority.Medium, ["medium", "средний"]),
        (Domain.Entities.TaskPriority.High, ["high", "высокий"]),
        (Domain.Entities.TaskPriority.Critical, ["critical", "критический"]));

    public static readonly IReadOnlyList<string> DateFunctions = ["today()", "startOfWeek()", "startOfMonth()", "startOfYear()"];

    public static readonly IReadOnlyList<string> LinkFunctions = ["linkedTo()", "blockedBy()", "isBlocked()"];

    /// <summary>Поля ORDER BY → колонка сортировки списка.</summary>
    public static readonly IReadOnlyDictionary<string, Flow.Shared.Contracts.Tasks.TaskSortField> Orders =
        new Dictionary<string, Flow.Shared.Contracts.Tasks.TaskSortField>(StringComparer.OrdinalIgnoreCase)
        {
            ["created"] = Flow.Shared.Contracts.Tasks.TaskSortField.Created,
            ["updated"] = Flow.Shared.Contracts.Tasks.TaskSortField.Updated,
            ["due"] = Flow.Shared.Contracts.Tasks.TaskSortField.Due,
            ["priority"] = Flow.Shared.Contracts.Tasks.TaskSortField.Priority,
            ["rank"] = Flow.Shared.Contracts.Tasks.TaskSortField.Rank,
            ["key"] = Flow.Shared.Contracts.Tasks.TaskSortField.Code,
            ["title"] = Flow.Shared.Contracts.Tasks.TaskSortField.Title,
            ["status"] = Flow.Shared.Contracts.Tasks.TaskSortField.Status,
            ["assignee"] = Flow.Shared.Contracts.Tasks.TaskSortField.Assignee
        };

    private static IReadOnlyDictionary<string, T> Map<T>(params (T Value, string[] Names)[] entries)
    {
        var map = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var (value, names) in entries)
            foreach (var name in names)
                map[name] = value;
        return map;
    }
}
