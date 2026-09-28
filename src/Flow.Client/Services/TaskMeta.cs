using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Client.Services;

/// <summary>
/// Подписи, глифы и цвета типов задач и приоритетов (docs/TZ_task_model.md, этап 1A). Вид типа, а не сам тип,
/// задаёт иконку: свой тип «Инцидент» вида Bug выглядит как ошибка. Цвета — только токены (tokens.css).
/// </summary>
public static class TaskMeta
{
    public static readonly IReadOnlyList<TaskTypeKind> Kinds =
        [TaskTypeKind.Epic, TaskTypeKind.Story, TaskTypeKind.Task, TaskTypeKind.Bug, TaskTypeKind.Subtask];

    /// <summary>От важного к неважному — в таком порядке их показывает меню.</summary>
    public static readonly IReadOnlyList<TaskPriority> Priorities =
        [TaskPriority.Critical, TaskPriority.High, TaskPriority.Medium, TaskPriority.Low, TaskPriority.None];

    public static string KindLabel(TaskTypeKind kind) => kind switch
    {
        TaskTypeKind.Epic => "Эпик",
        TaskTypeKind.Story => "История",
        TaskTypeKind.Task => "Задача",
        TaskTypeKind.Bug => "Ошибка",
        TaskTypeKind.Subtask => "Подзадача",
        _ => "—"
    };

    public static string KindIcon(TaskTypeKind kind) => kind switch
    {
        TaskTypeKind.Epic => "t-epic",
        TaskTypeKind.Story => "t-story",
        TaskTypeKind.Bug => "t-bug",
        TaskTypeKind.Subtask => "t-subtask",
        _ => "t-task"
    };

    public static string KindColor(TaskTypeKind kind) => kind switch
    {
        TaskTypeKind.Epic => "var(--tan)",
        TaskTypeKind.Story => "var(--sage)",
        TaskTypeKind.Bug => "var(--danger)",
        TaskTypeKind.Subtask => "rgba(255,255,255,0.5)",
        _ => "var(--accent)"
    };

    public static string PriorityLabel(TaskPriority priority) => priority switch
    {
        TaskPriority.Critical => "Критический",
        TaskPriority.High => "Высокий",
        TaskPriority.Medium => "Средний",
        TaskPriority.Low => "Низкий",
        _ => "Без приоритета"
    };

    /// <summary>null — у «без приоритета» глифа нет: пустое место в строке списка читается лучше лишнего значка.</summary>
    public static string? PriorityIcon(TaskPriority priority) => priority switch
    {
        TaskPriority.Critical => "p-critical",
        TaskPriority.High => "p-high",
        TaskPriority.Medium => "p-medium",
        TaskPriority.Low => "p-low",
        _ => null
    };

    public static string PriorityColor(TaskPriority priority) => priority switch
    {
        TaskPriority.Critical or TaskPriority.High => "var(--danger)",
        TaskPriority.Medium => "var(--accent)",
        TaskPriority.Low => "var(--sage)",
        _ => "var(--text-tertiary)"
    };

    /// <summary>
    /// Уровень иерархии вида (docs/TZ_task_model.md §3) — зеркало TaskTypeKind.Level() в домене: эпик 1 → история 2 →
    /// задача и ошибка 3 → подзадача 4. Родитель всегда строго выше ребёнка; сервер проверит то же.
    /// </summary>
    public static int Level(TaskTypeKind kind) => kind switch
    {
        TaskTypeKind.Epic => 1,
        TaskTypeKind.Story => 2,
        TaskTypeKind.Subtask => 4,
        _ => 3
    };

    public static int LevelOf(BoardResponse? board, Guid typeId) => TypeOf(board, typeId) is { } type ? Level(type.Kind) : 3;

    /// <summary>
    /// Тип для быстрой подзадачи: основной тип проекта, если он ниже родителя, иначе самый высокий из допустимых
    /// (у эпика — история, у истории — задача, у задачи — подзадача). null — ниже родителя ничего нет.
    /// </summary>
    public static TaskTypeResponse? ChildTypeFor(BoardResponse board, int parentLevel)
    {
        var allowed = board.TaskTypes.Where(t => !t.IsArchived && Level(t.Kind) > parentLevel).ToList();
        return allowed.FirstOrDefault(t => t.IsDefault) ?? allowed.OrderBy(t => Level(t.Kind)).FirstOrDefault();
    }

    /// <summary>Текст подтверждения удаления: с подзадачами удаляется всё поддерево (DELETE ?cascade=true).</summary>
    public static string DeleteWarning(TaskResponse task) => task.ChildCount > 0
        ? $"«{task.Title}» и все её подзадачи ({task.ChildCount} на первом уровне, вместе с их подзадачами) будут удалены без возможности восстановления."
        : $"«{task.Title}» будет удалена без возможности восстановления.";

    /// <summary>
    /// Ответы команд несут нулевые счётчики комментариев и подзадач (сервер их не пересчитывает): прежде чем
    /// подменить строку или карточку свежим ответом, счётчики берутся из того, что уже на экране.
    /// </summary>
    public static TaskResponse WithCountsOf(this TaskResponse fresh, TaskResponse known) =>
        fresh with { CommentCount = known.CommentCount, ChildCount = known.ChildCount, ChildDoneCount = known.ChildDoneCount, BlockedByCount = known.BlockedByCount };

    /// <summary>Подпись связи с точки зрения задачи (docs/TZ_task_model.md §5): «блокирует» / «заблокирована» и т.д.</summary>
    public static string LinkLabel(TaskLinkType type, bool outward) => (type, outward) switch
    {
        (TaskLinkType.Blocks, true) => "блокирует",
        (TaskLinkType.Blocks, false) => "заблокирована",
        (TaskLinkType.Duplicates, true) => "дублирует",
        (TaskLinkType.Duplicates, false) => "дублируется",
        (TaskLinkType.RelatesTo, _) => "связана с",
        (TaskLinkType.Clones, true) => "клон",
        (TaskLinkType.Clones, false) => "клонирована в",
        (TaskLinkType.SplitFrom, true) => "выделена из",
        (TaskLinkType.SplitFrom, false) => "разделена на",
        _ => type.ToString()
    };

    /// <summary>
    /// Связи, которые человек заводит руками. Clones и SplitFrom ставит сам сервер — при повторении и разделении
    /// задачи (этапы 1E/1F), руками их не предлагаем.
    /// </summary>
    public static readonly IReadOnlyList<(TaskLinkType Type, bool Outward)> ManualLinkKinds =
    [
        (TaskLinkType.Blocks, true), (TaskLinkType.Blocks, false), (TaskLinkType.RelatesTo, true),
        (TaskLinkType.Duplicates, true), (TaskLinkType.Duplicates, false)
    ];

    /// <summary>Тип задачи по Id в проекте; null — типа нет в загруженном проекте (удалён вместе с проектом и т.п.).</summary>
    public static TaskTypeResponse? TypeOf(BoardResponse? board, Guid typeId) =>
        board?.TaskTypes.FirstOrDefault(t => t.Id == typeId);

    /// <summary>Типы для выбора: без архивных, но текущий тип задачи остаётся, даже если его убрали в архив.</summary>
    public static IReadOnlyList<TaskTypeResponse> Selectable(IReadOnlyList<TaskTypeResponse> types, Guid? current = null) =>
        types.Where(t => !t.IsArchived || t.Id == current).ToList();
}
