namespace Flow.Domain.Entities;

/// <summary>
/// Вид типа задачи — общий для всех проектов ключ, как <see cref="StatusType"/> у статусов: сами типы принадлежат
/// проекту и называются как угодно, а фильтр «все ошибки во всех проектах» и уровень иерархии идут по виду.
/// Значения хранятся в БД как int — порядок не менять, только дописывать в конец (docs/TZ_task_model.md §1).
/// </summary>
public enum TaskTypeKind
{
    Epic = 0,
    Story = 1,
    Task = 2,
    Bug = 3,
    Subtask = 4
}

public static class TaskTypeKindExtensions
{
    /// <summary>
    /// Уровень иерархии: родитель всегда строго выше ребёнка (эпик 1 → история 2 → задача/ошибка 3 → подзадача 4).
    /// Выводится из вида, а не хранится у типа: свой тип «Улучшение» вида Task встаёт на уровень задачи сам.
    /// </summary>
    public static int Level(this TaskTypeKind kind) => kind switch
    {
        TaskTypeKind.Epic => 1,
        TaskTypeKind.Story => 2,
        TaskTypeKind.Task or TaskTypeKind.Bug => 3,
        TaskTypeKind.Subtask => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown task type kind.")
    };
}
